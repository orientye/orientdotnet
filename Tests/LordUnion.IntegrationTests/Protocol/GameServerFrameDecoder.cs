using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace LordUnion.IntegrationTests.Protocol;

public sealed class GameServerFrameDecoder : ByteToMessageDecoder
{
    private readonly int maxBodyLength;

    public GameServerFrameDecoder(int maxBodyLength = 1024 * 1024)
    {
        if (maxBodyLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBodyLength), maxBodyLength,
                "Max body length must be positive.");
        }

        this.maxBodyLength = maxBodyLength;
    }

    protected override void Decode(IChannelHandlerContext context, IByteBuffer input, List<object> output)
    {
        if (input.ReadableBytes < ServerPacketFrame.HeaderLength)
        {
            return;
        }

        input.MarkReaderIndex();
        var header0 = (uint)input.ReadIntLE();
        var bodyLength = input.ReadIntLE();
        if (bodyLength < 0)
        {
            throw new InvalidOperationException($"Invalid game-server body length {bodyLength}.");
        }

        if (bodyLength > maxBodyLength)
        {
            throw new InvalidOperationException(
                $"Game-server body length {bodyLength} exceeds maximum {maxBodyLength}.");
        }

        if (input.ReadableBytes < bodyLength)
        {
            input.ResetReaderIndex();
            return;
        }

        var body = new byte[bodyLength];
        input.ReadBytes(body);
        output.Add(new GameServerFrame(header0, body));
    }
}