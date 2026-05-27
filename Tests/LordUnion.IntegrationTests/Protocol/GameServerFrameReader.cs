using System.Net.Sockets;

namespace LordUnion.IntegrationTests.Protocol;

public static class GameServerFrameReader
{
    public static async Task<ServerPacketFrame> ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, ServerPacketFrame.HeaderLength, cancellationToken);
        var frameHeader = ServerPacketFrame.DecodeHeader(header);
        if (frameHeader.BodyLength < 0)
        {
            throw new InvalidOperationException($"Invalid body length {frameHeader.BodyLength}.");
        }

        var body = await ReadExactAsync(stream, frameHeader.BodyLength, cancellationToken);
        return new ServerPacketFrame(frameHeader.Header0, frameHeader.BodyLength, body);
    }

    public static async Task<byte[]> ReadExactAsync(
        NetworkStream stream,
        int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException($"Connection closed after reading {offset}/{length} bytes.");
            }

            offset += read;
        }

        return buffer;
    }
}
