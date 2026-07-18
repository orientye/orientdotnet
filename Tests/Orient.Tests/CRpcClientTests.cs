using Orient.Runtime;
using Orient.Rpc.Client;
using Orient.Rpc.Codec;
using Orient.Rpc.Transport;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace Orient.Tests;

public class CRpcClientTests : OrientTestBase
{
    [Fact]
    public void ConstructorThrowsWhenLoopIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CRpcClient(null!));
    }

    [Fact]
    public void CallAsyncClearsPendingCallWhenWriteFailsImmediately()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var client = new CRpcClient(executor);
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
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var client = new CRpcClient(executor);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 1));

        Assert.Contains("not connected", exception.Message);
    }

    [Fact]
    public void CallAsyncThrowsWhenNoOrientExecutorIsBound()
    {
        var executor = new OrientExecutor();
        Exception? exception = null;
        var worker = new Thread(() =>
        {
            var client = new CRpcClient(executor);
            exception = Assert.Throws<InvalidOperationException>(() =>
                client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 0));
        });

        worker.Start();
        worker.Join();

        Assert.Contains("OrientExecutor", exception!.Message);
    }

    [Fact]
    public void CallAsyncThrowsWhenCurrentLoopDiffersFromOwner()
    {
        var ownerLoop = new OrientExecutor();
        var otherLoop = new OrientExecutor();
        otherLoop.BindToCurrentThread();

        var client = new CRpcClient(ownerLoop);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 1));

        Assert.Contains("owner OrientExecutor", exception.Message);
    }

    [Fact]
    public void CallAsyncThrowsWhenTimeoutIsNotPositive()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var client = new CRpcClient(executor);
        SetClientHostChannel(client, new EmbeddedChannel());

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 0));

        Assert.Equal("timeout", exception.ParamName);
    }

    [Fact]
    public void ConnectAsyncThrowsWhenNotOnOwnerLoop()
    {
        var executor = new OrientExecutor();
        var client = new CRpcClient(executor);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            client.ConnectAsync("127.0.0.1", 7999));

        Assert.Contains("OrientExecutor", exception.Message);
    }

    [Fact]
    public void CallAsyncResponseContinuationRunsOnCallingLoop()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var loopThreadId = Environment.CurrentManagedThreadId;

        var client = new CRpcClient(executor);
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

        var response = CRpcTestMessages.CreateResponse(7, 8, reqSequence: 1);

        var worker = new Thread(() => client.OnReceiveResponse(response));
        worker.Start();
        worker.Join();

        Assert.Null(result);
        Assert.False(awaiter.IsCompleted);

        executor.Tick();

        Assert.Same(response, result);
        Assert.Equal(loopThreadId, continuationThreadId);
    }

    [Fact]
    public void CallAsyncMatchesOutOfOrderResponsesToPendingCalls()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var client = new CRpcClient(executor);
        SetClientHostChannel(client, new EmbeddedChannel());
        var firstTask = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
        var secondTask = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
        var firstAwaiter = firstTask.GetAwaiter();
        var secondAwaiter = secondTask.GetAwaiter();

        CRpcMessage? firstResult = null;
        CRpcMessage? secondResult = null;
        firstAwaiter.OnCompleted(() => firstResult = firstAwaiter.GetResult());
        secondAwaiter.OnCompleted(() => secondResult = secondAwaiter.GetResult());

        var firstResponse = CRpcTestMessages.CreateResponse(7, 8, reqSequence: 1);
        var secondResponse = CRpcTestMessages.CreateResponse(7, 8, reqSequence: 2);

        var worker = new Thread(() =>
        {
            client.OnReceiveResponse(secondResponse);
            client.OnReceiveResponse(firstResponse);
        });
        worker.Start();
        worker.Join();

        Assert.False(firstAwaiter.IsCompleted);
        Assert.False(secondAwaiter.IsCompleted);

        executor.Tick();

        Assert.Same(firstResponse, firstResult);
        Assert.Same(secondResponse, secondResult);
    }

    [Fact]
    public void CallAsyncResponsePostedBeforeDueTimeoutCompletesWithResult()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var client = new CRpcClient(executor);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 1);
        var awaiter = task.GetAwaiter();
        CRpcMessage? result = null;
        awaiter.OnCompleted(() => result = awaiter.GetResult());

        Thread.Sleep(20);

        var response = CRpcTestMessages.CreateResponse(7, 8, reqSequence: 1);
        client.OnReceiveResponse(response);

        executor.Tick();

        Assert.Same(response, result);
        Assert.True(awaiter.IsCompleted);
    }

    [Fact]
    public void CallAsyncLateResponseAfterTimeoutIsIgnored()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var client = new CRpcClient(executor);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 1);
        var awaiter = task.GetAwaiter();

        Thread.Sleep(20);
        executor.Tick();

        Assert.Throws<TimeoutException>(() => awaiter.GetResult());

        var lateResponse = CRpcTestMessages.CreateResponse(7, 8, reqSequence: 1);
        client.OnReceiveResponse(lateResponse);
        executor.Tick();

        Assert.Throws<TimeoutException>(() => awaiter.GetResult());
    }

    [Fact]
    public void CallAsyncTimeoutCompletesOnlyWhenCallingLoopTicks()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var client = new CRpcClient(executor);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 1);
        var awaiter = task.GetAwaiter();

        Thread.Sleep(20);

        Assert.False(awaiter.IsCompleted);

        executor.Tick();

        Assert.True(awaiter.IsCompleted);
        Assert.Throws<TimeoutException>(() => awaiter.GetResult());
    }

    [Fact]
    public void CloseAsyncFailsPendingCalls()
    {
        var executor = new OrientExecutor();
        var client = new CRpcClient(executor);
        var channel = new EmbeddedChannel();
        ConnectionClosedException? callException = null;

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
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
        var executor = new OrientExecutor();
        var client = new CRpcClient(executor);
        ConnectionClosedException? callException = null;

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
        {
            var channel = new EmbeddedChannel();
            SetClientHostChannel(client, channel);
            var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);

            GetClientHost(client).PostChannelInactive(channel);
            executor.Tick();

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
        var executor = new OrientExecutor();
        var client = new CRpcClient(executor);
        ConnectionClosedException? callException = null;
        var pipelineException = new InvalidOperationException("boom");

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
        {
            var channel = new EmbeddedChannel();
            SetClientHostChannel(client, channel);
            var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);

            GetClientHost(client).PostChannelException(channel, pipelineException);
            executor.Tick();

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
    public void ChannelInactiveRaisesConnectionLostOnOwnerLoop()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var client = new CRpcClient(executor);
        var channel = new EmbeddedChannel();
        SetClientHostChannel(client, channel);
        var lost = false;
        client.ConnectionLost += () => lost = true;

        GetClientHost(client).PostChannelInactive(channel);
        executor.Tick();

        Assert.True(lost);
    }

    [Fact]
    public void InboundHeartbeatIsIgnored()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var client = new CRpcClient(executor);
        SetClientHostChannel(client, new EmbeddedChannel());

        var task = client.CallAsync(1, 1, Array.Empty<byte>(), timeout: 5000);
        InvokeHostInboundMessage(client, CRpcMessage.CreateHeartbeat());
        executor.Tick();

        Assert.False(task.GetAwaiter().IsCompleted);
    }

    [Fact]
    public void StaleChannelInactiveDoesNotFailCurrentChannelPendingCalls()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var client = new CRpcClient(executor);
        var oldChannel = new EmbeddedChannel();
        var newChannel = new EmbeddedChannel();
        SetClientHostChannel(client, newChannel);
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
        var awaiter = task.GetAwaiter();

        GetClientHost(client).PostChannelInactive(oldChannel);
        executor.Tick();

        Assert.False(awaiter.IsCompleted);
    }

    [Fact]
    public void CloseAsyncClosesConnectedChannel()
    {
        var executor = new OrientExecutor();
        var client = new CRpcClient(executor);
        var channel = new EmbeddedChannel();

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
        {
            SetClientHostChannel(client, channel);
            await client.CloseAsync();
        });

        Assert.False(channel.Open);
    }

    [Fact]
    public void DisposeAsyncClosesConnectedChannel()
    {
        var executor = new OrientExecutor();
        var client = new CRpcClient(executor);
        var channel = new EmbeddedChannel();

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
        {
            SetClientHostChannel(client, channel);
            await client.DisposeAsync();
        });

        Assert.False(channel.Open);
    }

    [Fact]
    public void ConstructorAllowsTestHostInjection()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var options = new CRpcClientOptions();
        var host = new TcpChannelHost(executor, new CRpcClientPipelineFactory(options));

        var client = new CRpcClient(executor, options, host);

        Assert.Same(options, client.Options);
    }

    [Fact]
    public void HostInboundMessageCompletesPendingCall()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var client = new CRpcClient(executor);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
        var awaiter = task.GetAwaiter();
        CRpcMessage? result = null;
        awaiter.OnCompleted(() => result = awaiter.GetResult());

        GetClientHost(client).PostInboundMessage(CRpcTestMessages.CreateResponse(7, 8, reqSequence: 1));
        executor.Tick();

        Assert.True(awaiter.IsCompleted);
        Assert.Same(result, awaiter.GetResult());
    }

    [Fact]
    public void UnexpectedHostInboundMessageFailsPendingCalls()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var client = new CRpcClient(executor);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
        var awaiter = task.GetAwaiter();

        GetClientHost(client).PostInboundMessage(new object());
        executor.Tick();

        var exception = Assert.Throws<ConnectionClosedException>(() => awaiter.GetResult());
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void ClientLoopHostRunsUntilCancelled()
    {
        var executor = new OrientExecutor();
        using var cts = new CancellationTokenSource();

        var runner = new Thread(() => OrientExecutorHost.RunUntilCancelled(executor, cts.Token));
        runner.Start();

        var posted = new ManualResetEventSlim(false);
        executor.Post(() => posted.Set());

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
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var loopThreadId = Environment.CurrentManagedThreadId;
        var client = new CRpcClient(executor);

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
                return OrientTask.CompletedTask(context.Executor);
            });

        var push = CRpcTestMessages.CreatePush(10, 20, [1, 2, 3]);
        var worker = new Thread(() => client.OnReceiveResponse(push));
        worker.Start();
        worker.Join();

        Assert.Null(capturedContext);

        executor.Tick();

        Assert.NotNull(capturedContext);
        Assert.Same(executor, capturedContext!.Executor);
        Assert.Equal(10, capturedContext.ServiceId);
        Assert.Equal(20, capturedContext.MethodId);
        Assert.Equal([1, 2, 3], capturedBody);
        Assert.Equal(loopThreadId, handlerThreadId);
    }

    [Fact]
    public void PushMessageDoesNotCompletePendingCallWithSameSequence()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var client = new CRpcClient(executor);
        SetClientHostChannel(client, new EmbeddedChannel());
        var task = client.CallAsync(10, 20, Array.Empty<byte>(), timeout: 5000);
        var awaiter = task.GetAwaiter();

        client.OnReceiveResponse(CRpcTestMessages.CreatePush(10, 20, Array.Empty<byte>()));
        executor.Tick();

        Assert.False(awaiter.IsCompleted);
        Assert.Equal(1, GetPendingCallCount(client));
    }

    [Fact]
    public void UnknownPushInvokesUnhandledCallbackAndKeepsConnectionUsable()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var client = new CRpcClient(executor);
        CRpcPushContext? unhandled = null;
        client.OnUnhandledPush = context => unhandled = context;

        client.OnReceiveResponse(CRpcTestMessages.CreatePush(77, 88, [9]));
        executor.Tick();

        Assert.NotNull(unhandled);
        Assert.Equal(77, unhandled!.ServiceId);
        Assert.Equal(88, unhandled.MethodId);
    }

    [Fact]
    public void PushHandlerExceptionInvokesExceptionCallback()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var client = new CRpcClient(executor);
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

        client.OnReceiveResponse(CRpcTestMessages.CreatePush(1, 2, Array.Empty<byte>()));
        executor.Tick();

        Assert.Same(expected, captured);
        Assert.Equal(1, capturedContext!.ServiceId);
        Assert.Equal(2, capturedContext.MethodId);
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

    private static void InvokeHostInboundMessage(CRpcClient client, CRpcMessage message)
    {
        var method = typeof(CRpcClient).GetMethod(
            "OnHostInboundMessage",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(client, new object[] { message });
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
