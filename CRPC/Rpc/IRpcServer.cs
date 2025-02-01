namespace CRPC.Rpc
{
    public interface IRpcServer
    {
        public void Open();
        public void Close();
        public void RegisterService(IRpcService service);
        public void UnregisterService(IRpcService service);
    }
}
