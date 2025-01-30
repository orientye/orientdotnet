using CoreRPC.Rpc.CRpc.Codec;

namespace CoreRPC.Rpc
{
    public interface IRpcClient
    {
        public Task<CRpcMessage> CallAsync(short serviceId, short methodId, byte[] body, int timeout);
    }
}
