using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcConnection
{
    private readonly CRpcLoop ownerLoop;
    private readonly IChannel channel;
    private bool isActive = true;

    internal CRpcConnection(CRpcLoop ownerLoop, long id, IChannel channel)
    {
        this.ownerLoop = ownerLoop ?? throw new ArgumentNullException(nameof(ownerLoop));
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        ConnectionId = id;
    }

    public long ConnectionId { get; }

    public bool IsActive => isActive && channel.Active;

    public CRpcTask<bool> SendPushAsync(ushort serviceId, ushort methodId, byte[] body)
    {
        EnsureOwnerLoopThread();
        ArgumentNullException.ThrowIfNull(body);

        if (!IsActive)
        {
            return CRpcTask.FromResult(false, ownerLoop);
        }

        var header = CRpcMessageHeader.valueOf(
            CRpcMessageState.STATE_PUSH,
            resultCode: 0,
            sn: 0,
            module: serviceId,
            command: methodId);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        var message = CRpcMessage.valueOf(header, body);

        try
        {
            var writeTask = channel.WriteAndFlushAsync(message);
            return CompleteWriteAsync(writeTask);
        }
        catch
        {
            return CRpcTask.FromResult(false, ownerLoop);
        }
    }

    internal void MarkInactive()
    {
        EnsureOwnerLoopThread();
        isActive = false;
    }

    private async CRpcTask<bool> CompleteWriteAsync(System.Threading.Tasks.Task writeTask)
    {
        try
        {
            await CRpcTask.FromTask(writeTask, ownerLoop);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureOwnerLoopThread()
    {
        var loop = CRpcLoop.Current
            ?? throw new InvalidOperationException("CRpcConnection operations must be called from a bound CRpcLoop thread.");
        if (!ReferenceEquals(ownerLoop, loop))
        {
            throw new InvalidOperationException("CRpcConnection operations must be called on the connection's owner CRpcLoop thread.");
        }
    }
}
