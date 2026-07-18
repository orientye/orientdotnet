using System.Diagnostics.CodeAnalysis;
using Orient.Runtime;
using Orient.Rpc;

namespace Orient.Rpc.Server;

public sealed class RpcServiceRegistry
{
    private const int InitialServiceCapacity = 106;

    private readonly OrientExecutor executor;
    private readonly Dictionary<ushort, IRpcService> services = new(InitialServiceCapacity);

    public RpcServiceRegistry(OrientExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        this.executor = executor;
    }

    public void Register(IRpcService service)
    {
        executor.EnsureInExecutorThread();
        ArgumentNullException.ThrowIfNull(service);
        services[service.GetServiceId()] = service;
    }

    public bool TryGet(ushort serviceId, [MaybeNullWhen(false)] out IRpcService service)
    {
        executor.EnsureInExecutorThread();
        return services.TryGetValue(serviceId, out service);
    }

    public void Unregister(IRpcService service)
    {
        executor.EnsureInExecutorThread();
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
        executor.EnsureInExecutorThread();
        services.Clear();
    }
}
