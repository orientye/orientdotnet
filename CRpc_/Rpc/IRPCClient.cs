using CRpc.Rpc.CRpc.Codec;

namespace CRpc.Rpc
{
    public interface IRpcClient
    {
        public Task<CRpcMessage> CallAsync(short serviceId, short methodId, byte[] body, int timeout);
    }
}
