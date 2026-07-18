using System.Net.Sockets;
using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Protocol;
using Orient.Rpc.Codec;
using Orient.Rpc.Server;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace Orient.Tests;

public class CRpcServerHandlerTests : OrientTestBase
{
    private static int nextServiceId = 1000;

    private static EmbeddedChannel CreateHandlerChannel(
        CRpcServer server,
        IChannelHandler[]? headHandlers = null,
        IChannelHandler[]? tailHandlers = null)
    {
        var encoder = new CRpcMessageEncoder();
        var handlers = new List<IChannelHandler>((headHandlers?.Length ?? 0) + (tailHandlers?.Length ?? 0) + 2)
        {
            encoder,
            new CRpcServerHandler(server),
        };
        if (headHandlers is { Length: > 0 })
        {
            handlers.InsertRange(0, headHandlers);
        }

        if (tailHandlers is { Length: > 0 })
        {
            handlers.AddRange(tailHandlers);
        }

        return new EmbeddedChannel(handlers.ToArray());
    }

    [Fact]
    public void ChannelActiveRegistersConnection()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var server = new CRpcServer(executor);
        var channel = CreateHandlerChannel(server);

        channel.Pipeline.FireChannelActive();
        executor.Tick();

        var connection = Assert.Single(server.Connections.Snapshot());
        Assert.True(connection.IsActive);
    }

    [Fact]
    public void ChannelInactiveUnregistersConnection()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var server = new CRpcServer(executor);
        var channel = CreateHandlerChannel(server);
        channel.Pipeline.FireChannelActive();
        executor.Tick();
        var connection = Assert.Single(server.Connections.Snapshot());

        channel.Pipeline.FireChannelInactive();
        executor.Tick();

        Assert.Empty(server.Connections.Snapshot());
        Assert.False(connection.IsActive);
    }

    [Fact]
    public void ServiceReceivesCurrentConnectionInContext()
    {
        var executor = new OrientExecutor();
        var service = new ContextRecordingService(NextServiceId());
        var server = new CRpcServer(executor);
        RegisterOnServer(server, service);
        var channel = CreateHandlerChannel(server);

        channel.Pipeline.FireChannelActive();
        executor.Tick();
        Assert.False(channel.WriteInbound(CreateRequest(service.GetServiceId())));
        executor.Tick();

        Assert.NotNull(service.Connection);
        Assert.Equal(1, service.Connection!.ConnectionId);
    }

    [Fact]
    public void ChannelReadDispatchesServiceWorkToOrientExecutor()
    {
        var executor = new OrientExecutor();
        var service = new RecordingService(NextServiceId());
        var server = new CRpcServer(executor);
        RegisterOnServer(server, service);
        var channel = CreateHandlerChannel(server);
        ActivateChannel(executor, channel);

        Assert.False(channel.WriteInbound(CreateRequest(service.GetServiceId())));

        Assert.Equal(0, service.CallCount);

        executor.Tick();

        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public void HandlerUsesBoundLoopRegistry()
    {
        var serviceId = NextServiceId();
        var firstLoop = new OrientExecutor();
        var secondLoop = new OrientExecutor();
        var firstService = new RecordingService(serviceId);
        var secondService = new RecordingService(serviceId);
        var firstServer = new CRpcServer(firstLoop);

        var secondServer = new CRpcServer(secondLoop);

        DedicatedExecutorThread.Run(secondLoop, executor =>
        {
            executor.Post(() => secondServer.Services.Register(secondService));
            executor.Tick();
        });

        using var firstDriver = new ExecutorTestDriver(firstLoop);
        firstDriver.Run(() => firstLoop.Post(() => firstServer.Services.Register(firstService)));

        var channel = CreateHandlerChannel(firstServer);

        Assert.False(channel.WriteInbound(CreateRequest(serviceId)));

        Assert.Equal(0, firstService.CallCount);
        Assert.Equal(0, secondService.CallCount);

        firstDriver.Run(() =>
        {
            channel.Pipeline.FireChannelActive();
            firstLoop.Tick();
        });
        firstDriver.Run(() => firstLoop.Tick());

        Assert.Equal(1, firstService.CallCount);
        Assert.Equal(0, secondService.CallCount);
    }

    [Fact]
    public void ServiceLogicRunsOnOrientExecutorThread()
    {
        var executor = new OrientExecutor();
        var service = new RecordingService(NextServiceId());
        var server = new CRpcServer(executor);
        RegisterOnServer(server, service);
        var channel = CreateHandlerChannel(server);
        ActivateChannel(executor, channel);

        Assert.False(channel.WriteInbound(CreateRequest(service.GetServiceId())));
        var executorThreadId = Environment.CurrentManagedThreadId;

        executor.Tick();

        Assert.Equal(executorThreadId, service.LastThreadId);
        Assert.Same(executor, service.LastExecutor);
    }

    [Fact]
    public void ChannelReadDoesNotWaitForOutboundWriteCompletion()
    {
        var executor = new OrientExecutor();
        var service = new RecordingService(NextServiceId());
        var server = new CRpcServer(executor);
        RegisterOnServer(server, service);
        var delayedWrite = new DelayedWriteHandler();
        var channel = CreateHandlerChannel(server, headHandlers: new IChannelHandler[] { delayedWrite });
        ActivateChannel(executor, channel);

        Assert.False(channel.WriteInbound(CreateRequest(service.GetServiceId())));

        executor.Tick();

        Assert.Equal(1, service.CallCount);
        Assert.NotNull(delayedWrite.WrittenMessage);
        Assert.False(delayedWrite.WriteCompletion.Task.IsCompleted);
    }

    [Fact]
    public void ResponseWriteDoesNotMutateInboundRequest()
    {
        var executor = new OrientExecutor();
        var service = new RecordingService(NextServiceId());
        var server = new CRpcServer(executor);
        RegisterOnServer(server, service);
        var channel = CreateHandlerChannel(server);
        ActivateChannel(executor, channel);
        var request = CreateRequest(service.GetServiceId());

        Assert.False(channel.WriteInbound(request));

        executor.Tick();

        Assert.Equal(CRpcMessageType.Request, request.MessageType);
        Assert.Empty(request.Body);
    }

    [Fact]
    public void ConnectionResetByPeerIsHandledAsNormalDisconnect()
    {
        var exceptions = new ExceptionCaptureHandler();
        var server = new CRpcServer(new OrientExecutor());
        var channel = CreateHandlerChannel(server, tailHandlers: new IChannelHandler[] { exceptions });

        channel.Pipeline.FireExceptionCaught(new SocketException(10054));

        Assert.Null(exceptions.Exception);
    }

    [Fact]
    public void ChannelInactiveLogsNormalClientDisconnect()
    {
        var inactive = new InactiveCaptureHandler();
        var server = new CRpcServer(new OrientExecutor());
        var channel = CreateHandlerChannel(server, tailHandlers: new IChannelHandler[] { inactive });

        var output = ConsoleTestOutput.Capture(() => channel.Pipeline.FireChannelInactive());

        Assert.True(inactive.WasInactive);
        Assert.Contains("client disconnected", output);
    }

    [Fact]
    public void UnexpectedExceptionsContinueThroughPipeline()
    {
        var exceptions = new ExceptionCaptureHandler();
        var server = new CRpcServer(new OrientExecutor());
        var channel = CreateHandlerChannel(server, tailHandlers: new IChannelHandler[] { exceptions });
        var exception = new InvalidOperationException("boom");

        channel.Pipeline.FireExceptionCaught(exception);

        Assert.Same(exception, exceptions.Exception);
    }

    [Fact]
    public void HeartbeatIsIgnoredWithoutDispatchingToService()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var service = new RecordingService(NextServiceId());
        var server = new CRpcServer(executor);
        RegisterOnServer(server, service);
        var channel = CreateHandlerChannel(server);
        ActivateChannel(executor, channel);

        Assert.False(channel.WriteInbound(CRpcMessage.CreateHeartbeat()));
        executor.Tick();

        Assert.Equal(0, service.CallCount);
        Assert.Empty(channel.OutboundMessages);
    }

    [Fact]
    public void UnknownServiceWritesServiceNotFoundResponse()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var server = new CRpcServer(executor);
        var channel = CreateHandlerChannel(server);
        ActivateChannel(executor, channel);
        var request = CRpcTestMessages.CreateRequest(serviceId: 9999, methodId: 1, reqSequence: 42);

        Assert.False(channel.WriteInbound(request));
        executor.Tick();

        var response = ReadOutboundCrpcMessage(channel);
        Assert.Equal(CRpcMessageType.Response, response.MessageType);
        Assert.Equal(9999, response.ServiceId);
        Assert.Equal(1, response.MethodId);
        Assert.Equal(42, response.ReqSequence);
        Assert.Equal((int)CRpcStatusCode.ServiceNotFound, response.ResultCode);
        Assert.Empty(response.Body);
    }

    [Fact]
    public void UnknownMethodWritesMethodNotFoundResponse()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var service = new MethodRoutingService(NextServiceId());
        var server = new CRpcServer(executor);
        RegisterOnServer(server, service);
        var channel = CreateHandlerChannel(server);
        ActivateChannel(executor, channel);
        var request = CRpcTestMessages.CreateRequest(service.GetServiceId(), methodId: 99, reqSequence: 7);

        Assert.False(channel.WriteInbound(request));
        executor.Tick();

        var response = ReadOutboundCrpcMessage(channel);
        Assert.Equal(CRpcMessageType.Response, response.MessageType);
        Assert.Equal((int)CRpcStatusCode.MethodNotFound, response.ResultCode);
        Assert.Equal(7, response.ReqSequence);
    }

    [Fact]
    public void ServiceExceptionWritesInternalErrorResponse()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var service = new ThrowingService(NextServiceId());
        var server = new CRpcServer(executor);
        RegisterOnServer(server, service);
        var channel = CreateHandlerChannel(server);
        ActivateChannel(executor, channel);

        Assert.False(channel.WriteInbound(CreateRequest(service.GetServiceId())));
        executor.Tick();
        executor.Tick();

        var response = ReadOutboundCrpcMessage(channel);
        Assert.Equal(CRpcMessageType.Response, response.MessageType);
        Assert.Equal((int)CRpcStatusCode.InternalError, response.ResultCode);
    }

    private static CRpcMessage ReadOutboundCrpcMessage(EmbeddedChannel channel)
    {
        var outbound = channel.ReadOutbound<object>();
        return outbound is DotNetty.Buffers.IByteBuffer buffer
            ? CRpcMessage.ReadFrom(buffer)
            : (CRpcMessage)outbound!;
    }

    private static ushort NextServiceId()
    {
        return checked((ushort)Interlocked.Increment(ref nextServiceId));
    }

    private static CRpcMessage CreateRequest(ushort serviceId)
    {
        return CRpcTestMessages.CreateRequest(serviceId, methodId: 1, reqSequence: 1);
    }

    private static void RegisterOnServer(CRpcServer server, IRpcService service)
    {
        server.Executor.Post(() => server.Services.Register(service));
        server.Executor.Tick();
    }

    private static void ActivateChannel(OrientExecutor executor, EmbeddedChannel channel)
    {
        channel.Pipeline.FireChannelActive();
        executor.Tick();
    }

    private sealed class ContextRecordingService : IRpcService
    {
        private readonly ushort serviceId;

        public ContextRecordingService(ushort serviceId)
        {
            this.serviceId = serviceId;
        }

        public CRpcConnection? Connection { get; private set; }

        public ushort GetServiceId() => serviceId;

        public OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            Connection = ((CRpcContext)context).Connection;
            return OrientTask.FromResult((0, Array.Empty<byte>()), OrientExecutor.Current);
        }
    }

    private sealed class RecordingService : IRpcService
    {
        private readonly ushort serviceId;

        public RecordingService(ushort serviceId)
        {
            this.serviceId = serviceId;
        }

        public int CallCount { get; private set; }

        public int? LastThreadId { get; private set; }

        public OrientExecutor? LastExecutor { get; private set; }

        public ushort GetServiceId()
        {
            return serviceId;
        }

        public OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            CallCount++;
            LastThreadId = Environment.CurrentManagedThreadId;
            LastExecutor = OrientExecutor.Current;
            return OrientTask.FromResult((0, Array.Empty<byte>()), OrientExecutor.Current);
        }
    }

    private sealed class DelayedWriteHandler : ChannelHandlerAdapter
    {
        public TaskCompletionSource WriteCompletion { get; } = new();

        public object? WrittenMessage { get; private set; }

        public override Task WriteAsync(IChannelHandlerContext context, object message)
        {
            WrittenMessage = message;
            return WriteCompletion.Task;
        }
    }

    private sealed class ExceptionCaptureHandler : ChannelHandlerAdapter
    {
        public Exception? Exception { get; private set; }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Exception = exception;
        }
    }

    private sealed class InactiveCaptureHandler : ChannelHandlerAdapter
    {
        public bool WasInactive { get; private set; }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            WasInactive = true;
        }
    }

    private sealed class MethodRoutingService : IRpcService
    {
        private readonly ushort serviceId;

        public MethodRoutingService(ushort serviceId)
        {
            this.serviceId = serviceId;
        }

        public ushort GetServiceId() => serviceId;

        public OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            if (((CRpcMessage)req).MethodId == 1)
            {
                return OrientTask.FromResult((0, Array.Empty<byte>()), OrientExecutor.Current);
            }

            return OrientTask.FromResult(((int)CRpcStatusCode.MethodNotFound, Array.Empty<byte>()), OrientExecutor.Current);
        }
    }

    private sealed class ThrowingService : IRpcService
    {
        private readonly ushort serviceId;

        public ThrowingService(ushort serviceId)
        {
            this.serviceId = serviceId;
        }

        public ushort GetServiceId() => serviceId;

        public OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            throw new InvalidOperationException("boom");
        }
    }
}
