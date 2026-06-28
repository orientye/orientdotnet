using Orient.Runtime;

namespace Orient.Rpc
{
    public interface IRpcService
    {
        public ushort GetServiceId();
        
        public OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req);
    }
}
