using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Transport;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class CRpcClientTests : CrpcTestBase
{
    [Fact]
    public void ConstructorThrowsWhenLoopIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CRpcClient(null!));
    }

    [Fact]
    public void CallAsyncClearsPendingCallWhenWriteFailsImmediately()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        var writeFailure = new InvalidOperationException("write submission failed");
        SetClientHostChannel(client, new EmbeddedChannel(new ThrowOnWriteHandler(writeFailure)));

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 5000));

        Assert.Same(writeFailure, thrown);
        Assert.Equal(0, GetPendingCallCount(client));
    }

    [Fact]
    public void CallAsyncThrowsWhenNotConnected()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 1));

        Assert.Contains("not connected", exception.Message);
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
            client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 1));

        Assert.Contains("owner CRpcLoop", exception.Message);
    }

    [Fact]
    public void CallAsyncThrowsWhenTimeoutIsNotPositive()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        SetClientHostChannel(client, new EmbeddedChannel());

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 0));

        Assert.Equal("timeout", exception.ParamName);
    }

    [Fact]
    public void ConnectAsyncThrowsWhenNotOnOwnerLoop()
    {
        var loop = new CRpcLoop();
        var client = new CRpcClient(loop);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            client.ConnectAsync("127.0.0.1", 7999));

        Assert.Contains("CRpcLoop", exception.Message);
    }

    [Fact]
    public void CallAsyncResponseContinuationRunsOnCallingLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var loopThreadId = Environment.CurrentManagedThreadId;

        var client = new CRpcClient(loop);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
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
        SetClientHostChannel(client, new EmbeddedChannel());
        var firstTask = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
        var secondTask = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
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
    public void CallAsyncResponsePostedBeforeDueTimeoutCompletesWithResult()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 1);
        var awaiter = task.GetAwaiter();
        CRpcMessage? result = null;
        awaiter.OnCompleted(() => result = awaiter.GetResult());

        Thread.Sleep(20);

        var response = CreateResponse(reqSequence: 1);
        client.OnReceiveResponse(response);

        loop.Tick();

        Assert.Same(response, result);
        Assert.True(awaiter.IsCompleted);
    }

    [Fact]
    public void CallAsyncLateResponseAfterTimeoutIsIgnored()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 1);
        var awaiter = task.GetAwaiter();

        Thread.Sleep(20);
        loop.Tick();

        Assert.Throws<TimeoutException>(() => awaiter.GetResult());

        var lateResponse = CreateResponse(reqSequence: 1);
        client.OnReceiveResponse(lateResponse);
        loop.Tick();

        Assert.Throws<TimeoutException>(() => awaiter.GetResult());
    }

    [Fact]
    public void CallAsyncTimeoutCompletesOnlyWhenCallingLoopTicks()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 1);
        var awaiter = task.GetAwaiter();

        Thread.Sleep(20);

        Assert.False(awaiter.IsCompleted);

        loop.Tick();

        Assert.True(awaiter.IsCompleted);
        Assert.Throws<TimeoutException>(() => awaiter.GetResult());
    }

    [Fact]
    public void CloseAsyncFailsPendingCalls()
    {
        var loop = new CRpcLoop();
        var client = new CRpcClient(loop);
        var channel = new EmbeddedChannel();
        ConnectionClosedException? callException = null;

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            SetClientHostChannel(client, channel);
            var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);

            await client.CloseAsync();

            try
            {
                await task;
            }
            catch (ConnectionClosedException exception)
            {
                callException = exception;
            }
        });

        Assert.NotNull(callException);
        Assert.False(channel.Open);
    }

    [Fact]
    public void ChannelInactiveFailsPendingCalls()
    {
        var loop = new CRpcLoop();
        var client = new CRpcClient(loop);
        ConnectionClosedException? callException = null;

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            var channel = new EmbeddedChannel();
            SetClientHostChannel(client, channel);
            var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);

            GetClientHost(client).PostChannelInactive(channel);
            loop.Tick();

            try
            {
                await task;
            }
            catch (ConnectionClosedException exception)
            {
                callException = exception;
            }
        });

        Assert.NotNull(callException);
    }

    [Fact]
    public void ExceptionCaughtFailsPendingCalls()
    {
        var loop = new CRpcLoop();
        var client = new CRpcClient(loop);
        ConnectionClosedException? callException = null;
        var pipelineException = new InvalidOperationException("boom");

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            var channel = new EmbeddedChannel();
            SetClientHostChannel(client, channel);
            var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);

            GetClientHost(client).PostChannelException(channel, pipelineException);
            loop.Tick();

            try
            {
                await task;
            }
            catch (ConnectionClosedException exception)
            {
                callException = exception;
            }
        });

        Assert.NotNull(callException);
        Assert.Same(pipelineException, callException!.InnerException);
    }

    [Fact]
    public void StaleChannelInactiveDoesNotFailCurrentChannelPendingCalls()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        var oldChannel = new EmbeddedChannel();
        var newChannel = new EmbeddedChannel();
        SetClientHostChannel(client, newChannel);
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
        var awaiter = task.GetAwaiter();

        GetClientHost(client).PostChannelInactive(oldChannel);
        loop.Tick();

        Assert.False(awaiter.IsCompleted);
    }

    [Fact]
    public void CloseAsyncClosesConnectedChannel()
    {
        var loop = new CRpcLoop();
        var client = new CRpcClient(loop);
        var channel = new EmbeddedChannel();

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            SetClientHostChannel(client, channel);
            await client.CloseAsync();
        });

        Assert.False(channel.Open);
    }

    [Fact]
    public void DisposeAsyncClosesConnectedChannel()
    {
        var loop = new CRpcLoop();
        var client = new CRpcClient(loop);
        var channel = new EmbeddedChannel();

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            SetClientHostChannel(client, channel);
            await client.DisposeAsync();
        });

        Assert.False(channel.Open);
    }

    [Fact]
    public void ConstructorAllowsTestHostInjection()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var options = new CRpcClientOptions();
        var host = new TcpChannelHost(loop, new CRpcClientPipelineFactory(options));

        var client = new CRpcClient(loop, options, host);

        Assert.Same(options, client.Options);
    }

    [Fact]
    public void HostInboundMessageCompletesPendingCall()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
        var awaiter = task.GetAwaiter();
        CRpcMessage? result = null;
        awaiter.OnCompleted(() => result = awaiter.GetResult());

        GetClientHost(client).PostInboundMessage(CreateResponse(reqSequence: 1));
        loop.Tick();

        Assert.True(awaiter.IsCompleted);
        Assert.Same(result, awaiter.GetResult());
    }

    [Fact]
    public void UnexpectedHostInboundMessageFailsPendingCalls()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var client = new CRpcClient(loop);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
        var awaiter = task.GetAwaiter();

        GetClientHost(client).PostInboundMessage(new object());
        loop.Tick();

        var exception = Assert.Throws<ConnectionClosedException>(() => awaiter.GetResult());
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void ClientLoopHostRunsUntilCancelled()
    {
        var loop = new CRpcLoop();
        using var cts = new CancellationTokenSource();

        var runner = new Thread(() => CRpcClientLoopHost.RunUntilCancelled(loop, cts.Token));
        runner.Start();

        var posted = new ManualResetEventSlim(false);
        loop.Post(() => posted.Set());

        Assert.True(posted.Wait(TimeSpan.FromSeconds(5)));
        cts.Cancel();
        runner.Join(TimeSpan.FromSeconds(5));
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

    [Fact]
    public void PushMessageDispatchesRegisteredHandlerOnOwnerLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var loopThreadId = Environment.CurrentManagedThreadId;
        var client = new CRpcClient(loop);

        CRpcPushContext? capturedContext = null;
        byte[]? capturedBody = null;
        int? handlerThreadId = null;
        client.RegisterPushHandler(
            10,
            20,
            (context, body) =>
            {
                capturedContext = context;
                capturedBody = body;
                handlerThreadId = Environment.CurrentManagedThreadId;
                return CRpcTask.CompletedTask(context.Loop);
            });

        var push = CreatePush(serviceId: 10, methodId: 20, body: [1, 2, 3]);
        var worker = new Thread(() => client.OnReceiveResponse(push));
        worker.Start();
        worker.Join();

        Assert.Null(capturedContext);

        loop.Tick();

        Assert.NotNull(capturedContext);
        Assert.Same(loop, capturedContext!.Loop);
        Assert.Equal(10, capturedContext.ServiceId);
        Assert.Equal(20, capturedContext.MethodId);
        Assert.Equal([1, 2, 3], capturedBody);
        Assert.Equal(loopThreadId, handlerThreadId);
    }

    [Fact]
    public void PushMessageDoesNotCompletePendingCallWithSameSequence()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var client = new CRpcClient(loop);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(10, 20, Array.Empty<byte>(), timeout: 5000);
        var awaiter = task.GetAwaiter();

        client.OnReceiveResponse(CreatePush(10, 20, Array.Empty<byte>(), reqSequence: 1));
        loop.Tick();

        Assert.False(awaiter.IsCompleted);
        Assert.Equal(1, GetPendingCallCount(client));
    }

    [Fact]
    public void UnknownPushInvokesUnhandledCallbackAndKeepsConnectionUsable()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var client = new CRpcClient(loop);
        CRpcPushContext? unhandled = null;
        client.OnUnhandledPush = context => unhandled = context;

        client.OnReceiveResponse(CreatePush(77, 88, [9]));
        loop.Tick();

        Assert.NotNull(unhandled);
        Assert.Equal(77, unhandled!.ServiceId);
        Assert.Equal(88, unhandled.MethodId);
    }

    [Fact]
    public void PushHandlerExceptionInvokesExceptionCallback()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var client = new CRpcClient(loop);
        var expected = new InvalidOperationException("push failed");
        Exception? captured = null;
        CRpcPushContext? capturedContext = null;

        client.RegisterPushHandler(
            1,
            2,
            (context, body) => throw expected);
        client.OnPushException = (context, exception) =>
        {
            capturedContext = context;
            captured = exception;
        };

        client.OnReceiveResponse(CreatePush(1, 2, Array.Empty<byte>()));
        loop.Tick();

        Assert.Same(expected, captured);
        Assert.Equal(1, capturedContext!.ServiceId);
        Assert.Equal(2, capturedContext.MethodId);
    }

    private static CRpcMessage CreatePush(
        ushort serviceId,
        ushort methodId,
        byte[] body,
        long reqSequence = 0)
    {
        var header = CRpcMessageHeader.valueOf(
            CRpcMessageState.STATE_PUSH,
            resultCode: 0,
            sn: reqSequence,
            module: serviceId,
            command: methodId);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        return CRpcMessage.valueOf(header, body);
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

    private static int GetPendingCallCount(CRpcClient client)
    {
        var field = typeof(CRpcClient).GetField(
            "results",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var results = field!.GetValue(client);
        Assert.NotNull(results);
        return ((System.Collections.ICollection)results).Count;
    }

    private static TcpChannelHost GetClientHost(CRpcClient client)
    {
        var hostField = typeof(CRpcClient).GetField(
            "host",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(hostField);
        return Assert.IsType<TcpChannelHost>(hostField!.GetValue(client));
    }

    private static void SetClientHostChannel(CRpcClient client, EmbeddedChannel channel)
    {
        var host = GetClientHost(client);
        var channelField = typeof(TcpChannelHost).GetField(
            "channel",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(channelField);
        channelField!.SetValue(host, channel);
    }

    private sealed class ThrowOnWriteHandler : ChannelHandlerAdapter
    {
        private readonly Exception exception;

        public ThrowOnWriteHandler(Exception exception)
        {
            this.exception = exception;
        }

        public override Task WriteAsync(IChannelHandlerContext context, object message)
        {
            ReferenceCountUtil.Release(message);
            throw exception;
        }
    }
}
