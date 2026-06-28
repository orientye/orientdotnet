using System.Diagnostics.CodeAnalysis;
using Orient.Runtime;
using Orient.Rpc;

namespace Orient.Rpc.Server;

public sealed class RpcServiceRegistry
{
    private const int InitialServiceCapacity = 106;

    private readonly OrientLoop loop;
    private readonly Dictionary<ushort, IRpcService> services = new(InitialServiceCapacity);

    public RpcServiceRegistry(OrientLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        this.loop = loop;
    }

    public void Register(IRpcService service)
    {
        loop.EnsureInLoopThread();
        ArgumentNullException.ThrowIfNull(service);
        services[service.GetServiceId()] = service;
    }

    public bool TryGet(ushort serviceId, [MaybeNullWhen(false)] out IRpcService service)
    {
        loop.EnsureInLoopThread();
        return services.TryGetValue(serviceId, out service);
    }

    public void Unregister(IRpcService service)
    {
        loop.EnsureInLoopThread();
        ArgumentNullException.ThrowIfNull(service);
        var serviceId = service.GetServiceId();
        if (services.TryGetValue(serviceId, out var registeredService)
            && ReferenceEquals(registeredService, service))
        {
            services.Remove(serviceId);
        }
    }

    public void Clear()
    {
        loop.EnsureInLoopThread();
        services.Clear();
    }
}
