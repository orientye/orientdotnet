using CRpc.Async;

namespace CRpc.Rpc
{
    public interface IRpcService
    {
        public ushort GetServiceId();
        
        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req);
    }
}
