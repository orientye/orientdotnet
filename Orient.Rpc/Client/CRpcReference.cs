namespace Orient.Rpc.Client;

public static class CRpcReference
{
    public static CRpcReferenceBuilder<TProxy> For<TProxy>()
        where TProxy : class, new()
    {
        return new CRpcReferenceBuilder<TProxy>();
    }
}
