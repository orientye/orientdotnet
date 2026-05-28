using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;

namespace CRpc.Rpc
{
    public interface IRpcClient
    {
        public CRpcTask<CRpcMessage> CallAsync(ushort serviceId, ushort methodId, byte[] body, int timeout);

        public void RegisterPushHandler(ushort serviceId, ushort methodId, CRpcPushHandler handler);
    }
}
