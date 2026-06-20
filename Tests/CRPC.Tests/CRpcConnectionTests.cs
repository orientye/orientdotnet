using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class CRpcConnectionTests : CrpcTestBase
{
    [Fact]
    public void SendPushAsyncWritesStatePushMessage()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var channel = new EmbeddedChannel();
        var connection = new CRpcConnection(loop, id: 42, channel);

        var task = connection.SendPushAsync(1000, 2, [1, 2, 3]);
        var awaiter = task.GetAwaiter();

        Assert.True(awaiter.IsCompleted);
        Assert.True(awaiter.GetResult());

        var outbound = Assert.IsType<CRpcMessage>(channel.ReadOutbound<object>());
        Assert.Equal(CRpcMessageType.Push, outbound.MessageType);
        Assert.Equal(0, outbound.ReqSequence);
        Assert.Equal(1000, outbound.ServiceId);
        Assert.Equal(2, outbound.MethodId);
        Assert.Equal([1, 2, 3], outbound.Body);
    }

    [Fact]
    public void SendPushAsyncReturnsFalseWhenConnectionInactive()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var channel = new EmbeddedChannel();
        var connection = new CRpcConnection(loop, id: 42, channel);
        connection.MarkInactive();

        var awaiter = connection.SendPushAsync(1000, 2, Array.Empty<byte>()).GetAwaiter();

        Assert.True(awaiter.IsCompleted);
        Assert.False(awaiter.GetResult());
    }

    [Fact]
    public void SendPushAsyncThrowsWhenCalledOutsideOwnerLoop()
    {
        var loop = new CRpcLoop();
        var channel = new EmbeddedChannel();
        var connection = new CRpcConnection(loop, id: 42, channel);

        var otherLoop = new CRpcLoop();
        otherLoop.BindToCurrentThread();
        var exception = Assert.Throws<InvalidOperationException>(
            () => connection.SendPushAsync(1000, 2, Array.Empty<byte>()));

        Assert.Contains("owner CRpcLoop", exception.Message);
    }
}
