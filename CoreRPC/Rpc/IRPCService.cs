namespace CoreRPC.Rpc
{
    public interface IRpcService
    {
        public int GetServiceId();
        
        public Task<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req);
    }
}
