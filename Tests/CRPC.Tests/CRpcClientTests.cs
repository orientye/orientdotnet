using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;

namespace CRPC.Tests;

public class CRpcClientTests
{
    [Fact]
    public void CallAsyncThrowsWhenNoCRpcLoopIsBound()
    {
        var client = new CRpcClient();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 0));

        Assert.Contains("CRpcLoop", exception.Message);
    }

    [Fact]
    public void CallAsyncResponseContinuationRunsOnCallingLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var loopThreadId = Environment.CurrentManagedThreadId;

        var client = new CRpcClient();
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

        loop.Tick();

        Assert.Same(response, result);
        Assert.Equal(loopThreadId, continuationThreadId);
    }
}
