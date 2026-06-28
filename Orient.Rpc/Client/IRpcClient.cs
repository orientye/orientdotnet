using Orient.Runtime;
using Orient.Rpc.Client;
using Orient.Rpc.Codec;

namespace Orient.Rpc
{
    public interface IRpcClient
    {
        public OrientTask<CRpcMessage> CallAsync(ushort serviceId, ushort methodId, byte[] body, int timeout);

        public void RegisterPushHandler(ushort serviceId, ushort methodId, CRpcPushHandler handler);
    }
}
