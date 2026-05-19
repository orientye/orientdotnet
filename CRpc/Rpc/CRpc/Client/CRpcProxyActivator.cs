using System.Reflection;
using CRpc.Rpc;

namespace CRpc.Rpc.CRpc.Client;

public static class CRpcProxyActivator
{
    public static TProxy Create<TProxy>(IRpcClient rpcClient)
        where TProxy : class, new()
    {
        ArgumentNullException.ThrowIfNull(rpcClient);

        var proxy = new TProxy();
        var field = typeof(TProxy).GetField(
            "__client",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (field is null || !typeof(IRpcClient).IsAssignableFrom(field.FieldType))
        {
            throw new InvalidOperationException(
                $"{typeof(TProxy).FullName} must expose an IRpcClient field named __client.");
        }

        field.SetValue(proxy, rpcClient);
        return proxy;
    }
}
