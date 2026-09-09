using System.Text;

namespace SafeSpeak.Core.Connectors.TikTok;

// Small, bounded protobuf wire reader for the public LIVE experiment. Field
// mappings are documented in THIRD-PARTY-NOTICES.md; no generated SDK is loaded.
internal sealed class TikTokProtobuf
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly List<Field> _fields = [];
    private readonly record struct Field(int Tag, int Wire, ulong Number, ReadOnlyMemory<byte> Bytes);

    public TikTokProtobuf(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("TikTok decoded data exceeded the safety limit.");
        int offset = 0;
        while (offset < bytes.Length)
        {
            if (_fields.Count >= 4096) throw new InvalidDataException("Too many TikTok data fields.");
            ulong key = ReadVarint(bytes.Span, ref offset);
            ulong tag = key >> 3;
            if (tag is 0 or > 536870911) throw new InvalidDataException("Invalid TikTok data field.");
            int wire = (int)(key & 7);
            ulong number = 0;
            ReadOnlyMemory<byte> data = default;
            if (wire == 0) number = ReadVarint(bytes.Span, ref offset);
            else
            {
                ulong size = wire switch
                {
                    1 => 8,
                    2 => ReadVarint(bytes.Span, ref offset),
                    5 => 4,
                    _ => throw new InvalidDataException("Unsupported TikTok data encoding.")
                };
                if (size > (ulong)(bytes.Length - offset)) throw new InvalidDataException("Incomplete TikTok data.");
                data = bytes.Slice(offset, (int)size);
                offset += (int)size;
            }
            _fields.Add(new Field((int)tag, wire, number, data));
        }
    }

    public ulong Number(int tag) => _fields.LastOrDefault(f => f.Tag == tag && f.Wire == 0).Number;
    public ReadOnlyMemory<byte> Bytes(int tag) => _fields.LastOrDefault(f => f.Tag == tag && f.Wire == 2).Bytes;
    public IEnumerable<ReadOnlyMemory<byte>> Repeated(int tag) => _fields.Where(f => f.Tag == tag && f.Wire == 2).Select(f => f.Bytes);
    public TikTokProtobuf Message(int tag) => new(Bytes(tag));
    public string Text(int tag, int maxBytes = 4096)
    {
        var bytes = Bytes(tag);
        if (bytes.Length > maxBytes) throw new InvalidDataException("TikTok text exceeded the safety limit.");
        return StrictUtf8.GetString(bytes.Span);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ulong result = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            if (offset >= bytes.Length) throw new InvalidDataException("Incomplete TikTok integer.");
            byte value = bytes[offset++];
            if (shift == 63 && value > 1) throw new InvalidDataException("Invalid TikTok integer.");
            result |= (ulong)(value & 127) << shift;
            if ((value & 128) == 0) return result;
        }
        throw new InvalidDataException("Invalid TikTok integer.");
    }

    internal sealed class Writer
    {
        private readonly MemoryStream _stream = new();
        public Writer Number(int tag, ulong value) { Varint((ulong)tag << 3); Varint(value); return this; }
        public Writer Text(int tag, string value) => Bytes(tag, Encoding.UTF8.GetBytes(value));
        public Writer Bytes(int tag, ReadOnlySpan<byte> value)
        {
            Varint(((ulong)tag << 3) | 2); Varint((ulong)value.Length); _stream.Write(value); return this;
        }
        public byte[] Build() => _stream.ToArray();
        private void Varint(ulong value)
        {
            while (value >= 128) { _stream.WriteByte((byte)((value & 127) | 128)); value >>= 7; }
            _stream.WriteByte((byte)value);
        }
    }
}
