namespace GateWay;

public interface IBackendPicker
{
    BackendEndpoint? Pick(IReadOnlyList<BackendEndpoint> endpoints, ref int roundRobinCursor);
}

public sealed class RoundRobinPicker : IBackendPicker
{
    public BackendEndpoint? Pick(IReadOnlyList<BackendEndpoint> endpoints, ref int roundRobinCursor)
    {
        if (endpoints.Count == 0)
        {
            return null;
        }

        var healthy = endpoints.Where(endpoint => endpoint.IsHealthy).ToArray();
        if (healthy.Length == 0)
        {
            return null;
        }

        var index = roundRobinCursor % healthy.Length;
        roundRobinCursor = (roundRobinCursor + 1) % int.MaxValue;
        return healthy[index];
    }
}
