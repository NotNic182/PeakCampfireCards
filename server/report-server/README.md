# Campfire Cards report server

A tiny zero-dependency Node server that receives the mod's in-game bug reports
(the F9 window). One `POST /report` endpoint, reports stored as JSON lines on a
volume, an optional Discord summary per report.

## What a report contains (and what it never contains)

The mod builds the report; this server just stores it. A report carries the mod
and game version, the scene name, player count, host flag, card ids, the
tester's comment and categories, a random per-install id, and (when the tester
leaves the checkbox on) the tail of the BepInEx log **after scrubbing**:
user-folder paths, the OS user name, every nickname in the room, Steam ids, IP
addresses and e-mail addresses are replaced with placeholders before the report
leaves the tester's machine.

This server stores **no client addresses and no request headers**. The rate
limiter keeps a salted hash of the source address in memory for one minute and
the salt changes every boot.

## Deploy on Railway

1. railway.com → **New Project** → **Deploy from GitHub repo** → pick this repository.
2. In the service's **Settings → Root Directory**, enter `server/report-server`.
   Railway detects Node and uses `npm start` automatically.
3. **Add a volume** (right-click the service → Attach volume), mount path `/data`.
4. In **Variables**, set:
   - `DATA_DIR` = `/data`
   - `DISCORD_WEBHOOK_URL` = *(optional)* a Discord webhook; every report posts a short summary there (never the log).
   - `ADMIN_TOKEN` = *(optional)* any long random string; enables the read endpoints below.
   - `TRUST_PROXY` is detected automatically on Railway (`RAILWAY_ENVIRONMENT`); set `TRUST_PROXY=1` yourself behind
     any other reverse proxy, and leave it unset when clients connect directly — it controls which address the rate
     limiter trusts.
   - `MAX_STORE_BYTES` = *(optional)* cap on `reports.jsonl` before new reports are refused (default 256 MB).
5. **Settings → Networking → Generate Domain.** The mod's endpoint is then
   `https://<your-domain>/report` — put it in the mod's `Reporting.Endpoint`
   config default and rebuild, or in each tester's
   `BepInEx/config/notnic.campfirecards.cfg`.

## Endpoints

| Method and path | What it does |
|---|---|
| `POST /report` | Stores one report (1 MB cap, 6 per source per minute). Returns `{ok, id}`. |
| `GET /health` | `{ok:true}` — for uptime checks. |
| `GET /reports` | Lists stored reports (id, time, categories, first line of the comment). 404 unless `ADMIN_TOKEN` is set. |
| `GET /reports?id=<id>` | One full report, scrubbed log included. |

Authenticate the read endpoints with a header, so the token never lands in proxy or browser logs:

```bash
curl -H "Authorization: Bearer $ADMIN_TOKEN" https://<your-domain>/reports
```

(`?token=<ADMIN_TOKEN>` also works for a quick look from a browser; prefer the header.)

## Run it locally

```bash
node server.js
```

Then point the mod's `Reporting.Endpoint` at `http://localhost:3000/report`.
