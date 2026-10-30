// Campfire Cards report server - receives in-game bug reports from the mod.
// Zero dependencies, Node 18+. See README.md for deploying it on Railway.
//
// Privacy: the mod scrubs usernames, nicknames, Steam ids, addresses and e-mails
// before sending. This server never stores or logs a client address - the rate
// limiter keeps only a salted hash in memory for one minute.

'use strict';

const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const PORT = Number(process.env.PORT) || 3000;
const DATA_DIR = process.env.DATA_DIR || path.join(__dirname, 'data');
const WEBHOOK = (process.env.DISCORD_WEBHOOK_URL || '').trim();   // optional: a summary per report
const ADMIN_TOKEN = (process.env.ADMIN_TOKEN || '').trim();       // optional: enables GET /reports
// behind a reverse proxy (Railway) the proxy APPENDS the real client to x-forwarded-for, so the LAST
// entry is trustworthy and the first is attacker-chosen; off-proxy only the socket address is real
const TRUST_PROXY = process.env.TRUST_PROXY === '1' || !!process.env.RAILWAY_ENVIRONMENT;
const MAX_BODY = 1024 * 1024;              // 1 MB: a scrubbed log tail fits well inside
const MAX_STORE = Number(process.env.MAX_STORE_BYTES) || 256 * 1024 * 1024;   // stop appending at 256 MB
const RATE_WINDOW_MS = 60_000;
const RATE_MAX = 6;                        // reports per source per minute
const SALT = crypto.randomBytes(16);       // new every boot: hashes are never comparable across runs

fs.mkdirSync(DATA_DIR, { recursive: true });
const REPORTS_FILE = path.join(DATA_DIR, 'reports.jsonl');

// ------------------------------------------------------------------ rate limiting (hashed, in-memory only)
const buckets = new Map();
function sourceOf(req) {
  if (TRUST_PROXY) {
    const xff = req.headers['x-forwarded-for'];
    if (xff) {
      const parts = String(xff).split(',');
      return parts[parts.length - 1].trim();   // the proxy-appended entry; earlier ones are client-chosen
    }
  }
  return req.socket.remoteAddress || '?';
}

function allowed(addr) {
  const now = Date.now();
  const key = crypto.createHash('sha256').update(SALT).update(String(addr)).digest('base64').slice(0, 16);
  const hits = (buckets.get(key) || []).filter((t) => now - t < RATE_WINDOW_MS);
  if (hits.length >= RATE_MAX) { buckets.set(key, hits); return false; }
  hits.push(now);
  buckets.set(key, hits);
  if (buckets.size > 10_000) {
    // prune expired entries instead of wiping live throttles
    for (const [k, v] of buckets) if (now - v[v.length - 1] >= RATE_WINDOW_MS) buckets.delete(k);
  }
  return true;
}

// ------------------------------------------------------------------ helpers
function send(res, code, obj) {
  const body = JSON.stringify(obj);
  res.writeHead(code, { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) });
  res.end(body);
}

function isStr(v, max) { return typeof v === 'string' && v.length <= max; }
function isIntArray(v, max) { return Array.isArray(v) && v.length <= max && v.every((n) => Number.isInteger(n) && n >= 0 && n <= 255); }
function numOk(v, lo, hi) { return v == null || (typeof v === 'number' && Number.isFinite(v) && v >= lo && v <= hi); }

function validate(r) {
  if (typeof r !== 'object' || r === null || Array.isArray(r)) return 'not an object';
  if (r.v !== 1) return 'unknown version';
  if (!isStr(r.mod, 32) || !isStr(r.comment ?? '', 4000)) return 'bad mod or comment';
  if (r.game != null && !isStr(r.game, 32)) return 'bad game';
  if (r.scene != null && !isStr(r.scene, 64)) return 'bad scene';
  if (r.install != null && !isStr(r.install, 64)) return 'bad install';
  if (r.time != null && !isStr(r.time, 40)) return 'bad time';
  if (!numOk(r.balance, 0, 100000) || !numOk(r.players, 0, 100) || !numOk(r.strength, 0, 100)) return 'bad numbers';
  if (!Array.isArray(r.categories) || r.categories.length > 8 || !r.categories.every((c) => isStr(c, 32))) return 'bad categories';
  const deck = r.deck ?? [], teamDecks = r.teamDecks ?? [];
  if (!isIntArray(deck, 128)) return 'bad deck';
  if (!Array.isArray(teamDecks) || teamDecks.length > 16 || !teamDecks.every((d) => isIntArray(d, 128))) return 'bad teamDecks';
  if (r.log != null && !isStr(r.log, 600_000)) return 'bad log';
  if (!r.comment && r.categories.length === 0) return 'empty report';
  return null;
}

function tokenOk(req, url) {
  if (!ADMIN_TOKEN) return false;
  const header = String(req.headers['authorization'] || '');
  const given = header.startsWith('Bearer ') ? header.slice(7).trim() : (url.searchParams.get('token') || '');
  const a = crypto.createHash('sha256').update(given).digest();
  const b = crypto.createHash('sha256').update(ADMIN_TOKEN).digest();
  return crypto.timingSafeEqual(a, b);
}

async function notifyDiscord(id, r) {
  if (!WEBHOOK) return;
  const clip = (s, n) => String(s ?? '?').slice(0, n);
  const cats = r.categories.length ? r.categories.map((c) => clip(c, 32)).join(', ') : 'none';
  const lines = [
    `**Campfire Cards report \`${id}\`**`,
    `mod ${clip(r.mod, 32)} (balance ${clip(r.balance, 8)}) · game ${clip(r.game, 32)} · scene ${clip(r.scene, 64)} · ${clip(r.players, 4)} player(s)${r.host ? ' · host' : ''}`,
    `categories: ${cats} · deck: [${(r.deck ?? []).slice(0, 64).join(', ')}] · log: ${r.log ? 'attached (' + r.log.length + ' chars, stored)' : 'no'}`,
    r.comment ? '> ' + String(r.comment).slice(0, 500).replace(/\n/g, '\n> ') : '> (no comment)',
  ];
  try {
    await fetch(WEBHOOK, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: lines.join('\n').slice(0, 1990), allowed_mentions: { parse: [] } }),
      signal: AbortSignal.timeout(10_000),
    });
  } catch (e) {
    console.error('discord notify failed:', e.message);
  }
}

// ------------------------------------------------------------------ routes
const server = http.createServer((req, res) => {
  req.on('error', () => {});   // an aborted upload is dropped, never fatal
  res.on('error', () => {});
  const url = new URL(req.url, 'http://localhost');

  if (req.method === 'GET' && url.pathname === '/health') return send(res, 200, { ok: true });

  // admin reads, enabled only when ADMIN_TOKEN is set (Authorization: Bearer preferred; ?token= works too)
  if (req.method === 'GET' && url.pathname === '/reports') {
    if (!tokenOk(req, url)) return send(res, 404, { error: 'not found' });
    try {
      let lines = [];
      try { lines = fs.readFileSync(REPORTS_FILE, 'utf8').split('\n').filter(Boolean); } catch {}
      const parse = (l) => { try { return JSON.parse(l); } catch { return null; } };   // one torn line must not kill the listing
      const wanted = url.searchParams.get('id');
      if (wanted) {
        for (const l of lines) {
          const r = parse(l);
          if (r && r.id === wanted) return send(res, 200, r);
        }
        return send(res, 404, { error: 'no such report' });
      }
      const list = lines.map(parse).filter(Boolean).map((r) => ({
        id: r.id, received: r.received, mod: r.report.mod, scene: r.report.scene,
        categories: r.report.categories, comment: String(r.report.comment || '').slice(0, 120), log: !!r.report.log,
      }));
      return send(res, 200, { count: list.length, reports: list.reverse() });
    } catch (e) {
      console.error('listing failed:', e.message);
      return send(res, 500, { error: 'internal error' });
    }
  }

  if (req.method !== 'POST' || url.pathname !== '/report') return send(res, 404, { error: 'not found' });

  if (!allowed(sourceOf(req))) return send(res, 429, { error: 'too many reports, try again in a minute' });

  const declared = Number(req.headers['content-length'] || 0);
  if (declared > MAX_BODY) return send(res, 413, { error: 'report too large' });

  const chunks = [];
  let size = 0;
  req.on('data', (c) => {
    size += c.length;
    if (size > MAX_BODY) { send(res, 413, { error: 'report too large' }); req.destroy(); return; }
    chunks.push(c);
  });
  req.on('end', () => {
    if (res.writableEnded) return;
    try {
    let report;
    try { report = JSON.parse(Buffer.concat(chunks).toString('utf8')); }
    catch { return send(res, 400, { error: 'not json' }); }
    const bad = validate(report);
    if (bad) return send(res, 400, { error: bad });

    try {
      if (fs.existsSync(REPORTS_FILE) && fs.statSync(REPORTS_FILE).size > MAX_STORE) {
        console.error('storage cap reached; refusing new reports');
        return send(res, 503, { error: 'report storage is full' });
      }
    } catch {}

    const id = Date.now().toString(36) + '-' + crypto.randomBytes(3).toString('hex');
    const row = { id, received: new Date().toISOString(), report };   // no address, no headers
    try { fs.appendFileSync(REPORTS_FILE, JSON.stringify(row) + '\n'); }
    catch (e) { console.error('store failed:', e.message); return send(res, 500, { error: 'could not store the report' }); }
    console.log(`report ${id}: ${report.categories.join(',') || 'no category'} · mod ${report.mod} · comment ${String(report.comment || '').length} chars · log ${report.log ? report.log.length + ' chars' : 'no'}`);
    notifyDiscord(id, report);
    send(res, 200, { ok: true, id });
    } catch (e) {
      // one malformed request must never take the server down
      console.error('report handling failed:', e.message);
      if (!res.writableEnded) send(res, 500, { error: 'internal error' });
    }
  });
});

server.listen(PORT, () => console.log(`Campfire Cards report server on :${PORT}, storing in ${DATA_DIR}${TRUST_PROXY ? ', behind a trusted proxy' : ''}`));
