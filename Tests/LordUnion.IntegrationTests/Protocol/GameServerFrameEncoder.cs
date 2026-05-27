using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace LordUnion.IntegrationTests.Protocol;

public sealed class GameServerFrameEncoder : MessageToByteEncoder<GameServerFrame>
{
    protected override void Encode(IChannelHandlerContext context, GameServerFrame message, IByteBuffer output)
    {
        ArgumentNullException.ThrowIfNull(message.Body);

        output.WriteIntLE((int)message.Header0);
        output.WriteIntLE(message.Body.Length);
        output.WriteBytes(message.Body);
    }
}
