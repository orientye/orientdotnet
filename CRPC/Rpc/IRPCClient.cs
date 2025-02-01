using CRPC.Rpc.CRpc.Codec;

namespace CRPC.Rpc
{
    public interface IRpcClient
    {
        public Task<CRpcMessage> CallAsync(short serviceId, short methodId, byte[] body, int timeout);
    }
}
