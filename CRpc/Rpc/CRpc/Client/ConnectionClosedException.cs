namespace CRpc.Rpc.CRpc.Client;

public sealed class ConnectionClosedException : Exception
{
    public ConnectionClosedException(string message)
        : base(message)
    {
    }

    public ConnectionClosedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
