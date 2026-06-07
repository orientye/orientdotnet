namespace GateWay;

public sealed class BackendEndpoint
{
    public BackendEndpoint(string host, int port, int weight = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        Host = host;
        Port = port;
        Weight = weight;
    }

    public string Host { get; }

    public int Port { get; }

    public int Weight { get; }

    public bool IsHealthy { get; private set; } = true;

    public void MarkUnhealthy() => IsHealthy = false;

    public void MarkHealthy() => IsHealthy = true;

    public override string ToString() => $"{Host}:{Port}";
}
