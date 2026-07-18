using Orient.Runtime;
using Orient.Rpc.Codec;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Server;

public sealed class CRpcConnection
{
    private readonly OrientExecutor ownerExecutor;
    private readonly IChannel channel;
    private bool isActive = true;

    internal CRpcConnection(OrientExecutor ownerExecutor, long id, IChannel channel)
    {
        this.ownerExecutor = ownerExecutor ?? throw new ArgumentNullException(nameof(ownerExecutor));
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        ConnectionId = id;
    }

    public long ConnectionId { get; }

    public bool IsActive => isActive && channel.Active;

    public OrientTask<bool> SendPushAsync(ushort serviceId, ushort methodId, byte[] body)
    {
        EnsureOwnerExecutorThread();
        ArgumentNullException.ThrowIfNull(body);

        if (!IsActive)
        {
            return OrientTask.FromResult(false, ownerExecutor);
        }

        var message = CRpcMessage.Create(
            CRpcMessageType.Push,
            serviceId,
            methodId,
            reqSequence: 0,
            resultCode: 0,
            body);

        try
        {
            var writeTask = channel.WriteAndFlushAsync(message);
            return CompleteWriteAsync(writeTask);
        }
        catch
        {
            return OrientTask.FromResult(false, ownerExecutor);
        }
    }

    internal void MarkInactive()
    {
        EnsureOwnerExecutorThread();
        isActive = false;
    }

    private async OrientTask<bool> CompleteWriteAsync(System.Threading.Tasks.Task writeTask)
    {
        try
        {
            await OrientTask.FromTask(writeTask, ownerExecutor);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureOwnerExecutorThread()
    {
        var executor = OrientExecutor.Current
            ?? throw new InvalidOperationException("CRpcConnection operations must be called from a bound OrientExecutor thread.");
        if (!ReferenceEquals(ownerExecutor, executor))
        {
            throw new InvalidOperationException("CRpcConnection operations must be called on the connection's owner OrientExecutor thread.");
        }
    }
}
