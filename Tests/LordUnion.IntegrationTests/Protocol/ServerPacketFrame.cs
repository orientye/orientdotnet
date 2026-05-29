namespace LordUnion.IntegrationTests.Protocol;

public readonly struct ServerPacketFrame
{
    public const int HeaderLength = 8;
    public const uint ClientSendHeaderMagic = 0x14801;

    public ServerPacketFrame(uint header0, int bodyLength, byte[] body)
    {
        Header0 = header0;
        BodyLength = bodyLength;
        Body = body;
    }

    public uint Header0 { get; }
    public int BodyLength { get; }
    public byte[] Body { get; }

    public static ServerPacketFrame DecodeHeader(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length < HeaderLength)
        {
            throw new ArgumentException($"Header must be at least {HeaderLength} bytes.", nameof(headerBytes));
        }

        var header0 = BitConverter.ToUInt32(headerBytes);
        var bodyLength = BitConverter.ToInt32(headerBytes.Slice(4));
        return new ServerPacketFrame(header0, bodyLength, Array.Empty<byte>());
    }

    public static byte[] EncodeClientFrame(ReadOnlySpan<byte> body)
    {
        var packet = new byte[HeaderLength + body.Length];
        BitConverter.TryWriteBytes(packet.AsSpan(0, 4), ClientSendHeaderMagic);
        BitConverter.TryWriteBytes(packet.AsSpan(4, 4), body.Length);
        body.CopyTo(packet.AsSpan(HeaderLength));
        return packet;
    }

    public static byte[] EncodeFrame(uint header0, ReadOnlySpan<byte> body)
    {
        var packet = new byte[HeaderLength + body.Length];
        BitConverter.TryWriteBytes(packet.AsSpan(0, 4), header0);
        BitConverter.TryWriteBytes(packet.AsSpan(4, 4), body.Length);
        body.CopyTo(packet.AsSpan(HeaderLength));
        return packet;
    }
}