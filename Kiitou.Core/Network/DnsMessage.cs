using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Kiitou.Network;

public enum DnsRecordType : ushort
{
    A = 1,
    AAAA = 28
}

public sealed record DnsResponse(int ResponseCode, bool Truncated, IReadOnlyList<string> Addresses);

/// <summary>Builds and reads the small subset of DNS wire format needed for A and AAAA lookups.</summary>
public static class DnsMessage
{
    public const int NameError = 3;
    private const ushort ClassInternet = 1;

    public static byte[] BuildQuery(ushort id, string hostname, DnsRecordType type)
    {
        var bytes = new List<byte>(hostname.Length + 18);
        void WriteUInt16(ushort value)
        {
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        WriteUInt16(id);
        WriteUInt16(0x0100); // standard query, recursion desired
        WriteUInt16(1);      // one question
        WriteUInt16(0);
        WriteUInt16(0);
        WriteUInt16(0);
        foreach (string label in hostname.Split('.'))
        {
            byte[] ascii = Encoding.ASCII.GetBytes(label);
            if (ascii.Length is 0 or > 63) throw new ArgumentException("Invalid hostname label.", nameof(hostname));
            bytes.Add((byte)ascii.Length);
            bytes.AddRange(ascii);
        }
        bytes.Add(0);
        WriteUInt16((ushort)type);
        WriteUInt16(ClassInternet);
        return bytes.ToArray();
    }

    /// <exception cref="InvalidDataException">The message is malformed or is not the reply to this query.</exception>
    public static DnsResponse ParseResponse(ReadOnlySpan<byte> message, ushort expectedId, DnsRecordType type)
    {
        if (message.Length < 12) throw new InvalidDataException("DNS reply is too short.");
        if (BinaryPrimitives.ReadUInt16BigEndian(message) != expectedId)
            throw new InvalidDataException("DNS reply does not match the query.");

        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(message[2..]);
        if ((flags & 0x8000) == 0) throw new InvalidDataException("DNS message is not a reply.");
        int responseCode = flags & 0x000F;
        bool truncated = (flags & 0x0200) != 0;
        int questions = BinaryPrimitives.ReadUInt16BigEndian(message[4..]);
        int answers = BinaryPrimitives.ReadUInt16BigEndian(message[6..]);

        int position = 12;
        for (int i = 0; i < questions; i++)
        {
            position = SkipName(message, position) + 4;
            if (position > message.Length) throw new InvalidDataException("DNS reply question is cut off.");
        }

        var addresses = new List<string>();
        for (int i = 0; i < answers; i++)
        {
            // A truncated reply may legitimately end part-way through its answers.
            if (truncated && position >= message.Length) break;

            position = SkipName(message, position);
            if (position + 10 > message.Length) throw new InvalidDataException("DNS reply answer is cut off.");
            ushort recordType = BinaryPrimitives.ReadUInt16BigEndian(message[position..]);
            ushort recordClass = BinaryPrimitives.ReadUInt16BigEndian(message[(position + 2)..]);
            int dataLength = BinaryPrimitives.ReadUInt16BigEndian(message[(position + 8)..]);
            position += 10;
            if (position + dataLength > message.Length) throw new InvalidDataException("DNS reply data is cut off.");

            int expectedLength = type == DnsRecordType.A ? 4 : 16;
            if (recordType == (ushort)type && recordClass == ClassInternet && dataLength == expectedLength)
            {
                string address = new IPAddress(message.Slice(position, dataLength)).ToString();
                if (!addresses.Contains(address)) addresses.Add(address);
            }
            position += dataLength;
        }

        return new DnsResponse(responseCode, truncated, addresses);
    }

    private static int SkipName(ReadOnlySpan<byte> message, int position)
    {
        while (true)
        {
            if (position >= message.Length) throw new InvalidDataException("DNS reply name is cut off.");
            byte length = message[position];
            if (length == 0) return position + 1;
            if ((length & 0xC0) == 0xC0)
            {
                if (position + 2 > message.Length) throw new InvalidDataException("DNS reply name is cut off.");
                return position + 2;
            }
            if ((length & 0xC0) != 0) throw new InvalidDataException("DNS reply name uses an unsupported label type.");
            position += 1 + length;
        }
    }
}
