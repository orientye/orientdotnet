using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;

namespace CRpc.Rpc
{
    public interface IRpcClient
    {
        public CRpcTask<CRpcMessage> CallAsync(short serviceId, short methodId, byte[] body, int timeout);
    }
}
