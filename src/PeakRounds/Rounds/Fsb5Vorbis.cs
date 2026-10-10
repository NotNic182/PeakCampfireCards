using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NVorbis;
using NVorbis.Contracts;

namespace PeakRounds.Rounds
{
    /// <summary>
    /// systems.json Audio: decodes a Unity AudioClip's FSB5 blob (FMOD Sound Bank, Vorbis codec) into PCM.
    /// FMOD strips the Vorbis setup header and stores only its CRC32; the header is restored from the table that
    /// python-fsb5 (MIT, Simon Pinfold) collected, embedded as vorbis_headers.bin. Packets are then fed straight into
    /// NVorbis (MIT, Andrew Ward) through an in-memory packet provider, so no Ogg container is needed.
    /// </summary>
    public static class Fsb5Vorbis
    {
        private static Dictionary<uint, byte[]> _headers;
        private static readonly object Lock = new object();

        public static bool TryDecode(byte[] fsb, out float[] pcm, out int channels, out int rate, out string error)
        {
            pcm = null; channels = 0; rate = 0; error = null;
            try
            {
                var sample = ParseFirstSample(fsb);
                if (sample.Format != 15) { error = "FSB5 format " + sample.Format + " is not Vorbis"; return false; }
                var table = Headers();
                byte[] setup;
                if (!table.TryGetValue(sample.SetupCrc32, out setup)) { error = "no Vorbis setup header for crc32 " + sample.SetupCrc32; return false; }

                var packets = new List<byte[]>();
                packets.Add(IdHeader(sample.Channels, sample.Frequency));
                packets.Add(CommentHeader());
                packets.Add(setup);
                int pos = 0;
                var d = sample.Data;
                while (pos + 2 <= d.Length)
                {
                    int len = d[pos] | (d[pos + 1] << 8);
                    pos += 2;
                    if (len == 0 || pos + len > d.Length) break;
                    var p = new byte[len];
                    Buffer.BlockCopy(d, pos, p, 0, len);
                    packets.Add(p);
                    pos += len;
                }
                if (packets.Count <= 3) { error = "no audio packets"; return false; }

                var provider = new MemoryPacketProvider(packets);
                using (var dec = new StreamDecoder(provider))
                {
                    dec.ClipSamples = true;
                    channels = dec.Channels; rate = dec.SampleRate;
                    var all = new List<float>(Math.Max(1024, (int)sample.SampleCount * channels));
                    var buf = new float[4096 * Math.Max(1, channels)];
                    int n;
                    int guard = 0;
                    while ((n = dec.Read(buf, 0, buf.Length)) > 0 && guard++ < 100000)
                        for (int i = 0; i < n; i++) all.Add(buf[i]);
                    pcm = all.ToArray();
                }
                if (pcm.Length == 0) { error = "decoded 0 samples"; return false; }
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------ FSB5 container
        private struct Fsb5Sample
        {
            public int Format;
            public int Channels;
            public int Frequency;
            public long SampleCount;
            public uint SetupCrc32;
            public byte[] Data;
        }

        private static readonly int[] FrequencyTable = { 0, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000 };

        private static Fsb5Sample ParseFirstSample(byte[] fsb)
        {
            using (var ms = new MemoryStream(fsb))
            using (var r = new BinaryReader(ms))
            {
                if (fsb.Length < 60 || fsb[0] != (byte)'F' || fsb[1] != (byte)'S' || fsb[2] != (byte)'B' || fsb[3] != (byte)'5')
                    throw new InvalidDataException("not an FSB5 blob");
                ms.Position = 4;
                uint version = r.ReadUInt32();
                int numSamples = r.ReadInt32();
                int sampleHeadersSize = r.ReadInt32();
                int nameTableSize = r.ReadInt32();
                int dataSize = r.ReadInt32();
                int mode = r.ReadInt32();
                ms.Position += 8 + 16 + 8;
                if (version == 0) ms.Position += 4;
                long headerSize = ms.Position;
                if (numSamples < 1) throw new InvalidDataException("FSB5 with no samples");

                ulong raw = r.ReadUInt64();
                bool nextChunk = (raw & 1) != 0;
                int freqIndex = (int)((raw >> 1) & 0xF);
                int channels = (int)((raw >> 5) & 1) + 1;
                long dataOffset = (long)((raw >> 6) & 0xFFFFFFF) * 16;
                long sampleCount = (long)((raw >> 34) & 0x3FFFFFFF);
                int frequency = freqIndex < FrequencyTable.Length ? FrequencyTable[freqIndex] : 0;
                uint crc = 0;
                while (nextChunk)
                {
                    uint c = r.ReadUInt32();
                    nextChunk = (c & 1) != 0;
                    int size = (int)((c >> 1) & 0xFFFFFF);
                    int type = (int)((c >> 25) & 0x7F);
                    long end = ms.Position + size;
                    if (type == 11) crc = r.ReadUInt32();           // VORBISDATA
                    else if (type == 2) frequency = r.ReadInt32();  // FREQUENCY
                    else if (type == 1) channels = r.ReadByte();    // CHANNELS
                    ms.Position = end;
                }
                // second sample's offset bounds the first sample's data
                long dataEnd = dataSize;
                if (numSamples > 1)
                {
                    ulong raw2 = r.ReadUInt64();
                    dataEnd = (long)((raw2 >> 6) & 0xFFFFFFF) * 16;
                }
                long dataStart = headerSize + sampleHeadersSize + nameTableSize + dataOffset;
                long len = Math.Min(dataEnd - dataOffset, fsb.Length - dataStart);
                if (len <= 0) throw new InvalidDataException("empty FSB5 sample");
                var data = new byte[len];
                Buffer.BlockCopy(fsb, (int)dataStart, data, 0, (int)len);
                return new Fsb5Sample { Format = mode, Channels = channels, Frequency = frequency, SampleCount = sampleCount, SetupCrc32 = crc, Data = data };
            }
        }

        // ------------------------------------------------------------------ Vorbis headers
        private static Dictionary<uint, byte[]> Headers()
        {
            lock (Lock)
            {
                if (_headers != null) return _headers;
                var table = new Dictionary<uint, byte[]>();
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PeakRounds.vorbis_headers.bin"))
                {
                    if (s == null) throw new FileNotFoundException("embedded vorbis_headers.bin missing");
                    using (var r = new BinaryReader(s))
                    {
                        var magic = r.ReadBytes(4);
                        if (Encoding.ASCII.GetString(magic) != "VHT1") throw new InvalidDataException("bad vorbis_headers.bin");
                        int count = r.ReadInt32();
                        for (int i = 0; i < count; i++)
                        {
                            uint crc = r.ReadUInt32();
                            int len = r.ReadInt32();
                            table[crc] = r.ReadBytes(len);
                        }
                    }
                }
                _headers = table;
                return table;
            }
        }

        private static byte[] IdHeader(int channels, int rate)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)1);
                w.Write(Encoding.ASCII.GetBytes("vorbis"));
                w.Write(0u);                 // version
                w.Write((byte)channels);
                w.Write((uint)rate);
                w.Write(0); w.Write(0); w.Write(0);   // bitrate max / nominal / min
                w.Write((byte)(8 | (11 << 4)));       // blocksize_0 = 2^8 (256), blocksize_1 = 2^11 (2048): FMOD's fixed sizes
                w.Write((byte)1);                     // framing bit
                return ms.ToArray();
            }
        }

        private static byte[] CommentHeader()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)3);
                w.Write(Encoding.ASCII.GetBytes("vorbis"));
                var vendor = Encoding.UTF8.GetBytes("PeakRounds");
                w.Write((uint)vendor.Length); w.Write(vendor);
                w.Write(0u);        // user comments
                w.Write((byte)1);   // framing
                return ms.ToArray();
            }
        }

        // ------------------------------------------------------------------ NVorbis in-memory packets
        private sealed class MemoryPacket : DataPacket
        {
            private readonly byte[] _data;
            private int _pos;
            public MemoryPacket(byte[] data) { _data = data; }
            protected override int TotalBits => _data.Length * 8;
            protected override int ReadNextByte() => _pos < _data.Length ? _data[_pos++] : -1;
            public override void Reset() { base.Reset(); _pos = 0; }
        }

        private sealed class MemoryPacketProvider : NVorbis.Contracts.IPacketProvider
        {
            private readonly List<byte[]> _packets;
            private int _index;
            public MemoryPacketProvider(List<byte[]> packets) { _packets = packets; }
            public bool CanSeek => false;
            public int StreamSerial => 1;
            public NVorbis.Contracts.IPacket GetNextPacket()
            {
                if (_index >= _packets.Count) return null;
                var p = new MemoryPacket(_packets[_index]) { IsEndOfStream = _index == _packets.Count - 1 };
                _index++;
                return p;
            }
            public NVorbis.Contracts.IPacket PeekNextPacket()
            {
                if (_index >= _packets.Count) return null;
                return new MemoryPacket(_packets[_index]) { IsEndOfStream = _index == _packets.Count - 1 };
            }
            public long SeekTo(long granulePos, int preRoll, NVorbis.Contracts.GetPacketGranuleCount getPacketGranuleCount) => throw new NotSupportedException();
            public long GetGranuleCount() => 0;
        }
    }
}
