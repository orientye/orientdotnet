using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Codec;

public sealed class CRpcMessageEncoder : MessageToByteEncoder<CRpcMessage>
{
    private readonly int hashLength;
    private readonly int compressThreshold;

    public CRpcMessageEncoder(int hashLength, int compressThreshold)
    {
        this.hashLength = hashLength;
        this.compressThreshold = compressThreshold;
    }

    protected override void Encode(IChannelHandlerContext context, CRpcMessage message, IByteBuffer output)
    {
        ArgumentNullException.ThrowIfNull(message);
        PrepareMessage(message);
        message.toFrame(output, hashLength);
    }

    private void PrepareMessage(CRpcMessage message)
    {
        message.encryptAndCompress(compressThreshold, encrypt: true, compressWithoutEncrypt: true);
    }
}
