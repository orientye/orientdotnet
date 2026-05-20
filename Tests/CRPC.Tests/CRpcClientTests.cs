using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class CRpcClientTests
{
    [Fact]
    public void ConstructorThrowsWhenLoopIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CRpcClient(null!));
    }

    [Fact]
    public void CallAsyncThrowsWhenNoCRpcLoopIsBound()
    {
        var loop = new CRpcLoop();
        Exception? exception = null;
        var worker = new Thread(() =>
        {
            var client = new CRpcClient(loop);
            exception = Assert.Throws<InvalidOperationException>(() =>
                client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 0));
        });

        worker.Start();
        worker.Join();

        Assert.Contains("CRpcLoop", exception!.Message);
    }

    [Fact]
    public void CallAsyncThrowsWhenCurrentLoopDiffersFromOwner()
    {
        var ownerLoop = new CRpcLoop();
        var otherLoop = new CRpcLoop();
        otherLoop.BindToCurrentThread();

        var client = new CRpcClient(ownerLoop);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 0));

        Assert.Contains("owner CRpcLoop", exception.Message);
    }

    [Fact]
    public void CallAsyncResponseContinuationRunsOnCallingLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var loopThreadId = Environment.CurrentManagedThreadId;

        var client = new CRpcClient(loop);
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 0);
        var awaiter = task.GetAwaiter();

        int? continuationThreadId = null;
        CRpcMessage? result = null;
        awaiter.OnCompleted(() =>
        {
            result = awaiter.GetResult();
            continuationThreadId = Environment.CurrentManagedThreadId;
        });

        var responseHeader = CRpcMessageHeader.valueOf(
            CRpcMessageState.STATE_RESPONSE,
            resultCode: 0,
            sn: 1,
            module: 7,
            command: 8);
        var response = CRpcMessage.valueOf(responseHeader, Array.Empty<byte>());

        var worker = new Thread(() => client.OnReceiveResponse(response));
        worker.Start();
        worker.Join();

        Assert.Null(result);
        Assert.False(awaiter.IsCompleted);

        loop.Tick();

        Assert.Same(response, result);
        Assert.Equal(loopThreadId, continuationThreadId);
    }

    [Fact]
    public void CallAsyncMatchesOutOfOrderResponsesToPendingCalls()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        var firstTask = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 0);
        var secondTask = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 0);
        var firstAwaiter = firstTask.GetAwaiter();
        var secondAwaiter = secondTask.GetAwaiter();

        CRpcMessage? firstResult = null;
        CRpcMessage? secondResult = null;
        firstAwaiter.OnCompleted(() => firstResult = firstAwaiter.GetResult());
        secondAwaiter.OnCompleted(() => secondResult = secondAwaiter.GetResult());

        var firstResponse = CreateResponse(reqSequence: 1);
        var secondResponse = CreateResponse(reqSequence: 2);

        var worker = new Thread(() =>
        {
            client.OnReceiveResponse(secondResponse);
            client.OnReceiveResponse(firstResponse);
        });
        worker.Start();
        worker.Join();

        Assert.False(firstAwaiter.IsCompleted);
        Assert.False(secondAwaiter.IsCompleted);

        loop.Tick();

        Assert.Same(firstResponse, firstResult);
        Assert.Same(secondResponse, secondResult);
    }

    [Fact]
    public void CallAsyncTimeoutCompletesOnlyWhenCallingLoopTicks()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 1);
        var awaiter = task.GetAwaiter();

        Thread.Sleep(20);

        Assert.False(awaiter.IsCompleted);

        loop.Tick();

        Assert.True(awaiter.IsCompleted);
        Assert.Throws<TimeoutException>(() => awaiter.GetResult());
    }

    [Fact]
    public async Task CloseAsyncClosesConnectedChannel()
    {
        var client = new CRpcClient(new CRpcLoop());
        var channel = new EmbeddedChannel();
        SetClientChannel(client, channel);

        await client.CloseAsync();

        Assert.False(channel.Open);
    }

    [Fact]
    public async Task DisposeAsyncClosesConnectedChannel()
    {
        var client = new CRpcClient(new CRpcLoop());
        var channel = new EmbeddedChannel();
        SetClientChannel(client, channel);

        await client.DisposeAsync();

        Assert.False(channel.Open);
    }

    [Fact]
    public void PendingResultsUseLoopOwnedDictionary()
    {
        var field = typeof(CRpcClient).GetField(
            "results",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Equal(typeof(Dictionary<,>), field!.FieldType.GetGenericTypeDefinition());
    }

    private static CRpcMessage CreateResponse(long reqSequence)
    {
        var responseHeader = CRpcMessageHeader.valueOf(
            CRpcMessageState.STATE_RESPONSE,
            resultCode: 0,
            sn: reqSequence,
            module: 7,
            command: 8);
        return CRpcMessage.valueOf(responseHeader, Array.Empty<byte>());
    }

    private static void SetClientChannel(CRpcClient client, EmbeddedChannel channel)
    {
        var field = typeof(CRpcClient).GetField(
            "channel",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(client, channel);
    }
}
