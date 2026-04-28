using CRpc.Async;

namespace CRpc.Rpc
{
    public interface IRpcService
    {
        public int GetServiceId();
        
        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req);
    }
}
