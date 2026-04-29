using System.Net.Sockets;
using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class CRpcServerHandlerTests
{
    private static int nextServiceId = 1000;

    [Fact]
    public void ChannelReadDispatchesServiceWorkToCRpcLoop()
    {
        var service = new RecordingService(NextServiceId());
        var server = new CRpcServer();
        server.RegisterService(service);
        var channel = new EmbeddedChannel(new CRpcServerHandler());

        Assert.True(channel.WriteInbound(CreateRequest(service.GetServiceId())));

        Assert.Equal(0, service.CallCount);

        CRpcLoop.Main.Tick();

        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public void ServiceLogicRunsOnCRpcLoopThread()
    {
        var service = new RecordingService(NextServiceId());
        var server = new CRpcServer();
        server.RegisterService(service);
        var channel = new EmbeddedChannel(new CRpcServerHandler());

        Assert.True(channel.WriteInbound(CreateRequest(service.GetServiceId())));
        var loopThreadId = Environment.CurrentManagedThreadId;

        CRpcLoop.Main.Tick();

        Assert.Equal(loopThreadId, service.LastThreadId);
        Assert.Same(CRpcLoop.Main, service.LastLoop);
    }

    [Fact]
    public void ChannelReadDoesNotWaitForOutboundWriteCompletion()
    {
        var service = new RecordingService(NextServiceId());
        var server = new CRpcServer();
        server.RegisterService(service);
        var delayedWrite = new DelayedWriteHandler();
        var channel = new EmbeddedChannel(delayedWrite, new CRpcServerHandler());

        Assert.True(channel.WriteInbound(CreateRequest(service.GetServiceId())));

        CRpcLoop.Main.Tick();

        Assert.Equal(1, service.CallCount);
        Assert.NotNull(delayedWrite.WrittenMessage);
        Assert.False(delayedWrite.WriteCompletion.Task.IsCompleted);
    }

    [Fact]
    public void ConnectionResetByPeerIsHandledAsNormalDisconnect()
    {
        var exceptions = new ExceptionCaptureHandler();
        var channel = new EmbeddedChannel(new CRpcServerHandler(), exceptions);

        channel.Pipeline.FireExceptionCaught(new SocketException(10054));

        Assert.Null(exceptions.Exception);
    }

    [Fact]
    public void ChannelInactiveLogsNormalClientDisconnect()
    {
        var originalOut = Console.Out;
        using var output = new StringWriter();
        var inactive = new InactiveCaptureHandler();
        var channel = new EmbeddedChannel(new CRpcServerHandler(), inactive);

        try
        {
            Console.SetOut(output);

            channel.Pipeline.FireChannelInactive();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.True(inactive.WasInactive);
        Assert.Contains("client disconnected", output.ToString());
    }

    [Fact]
    public void UnexpectedExceptionsContinueThroughPipeline()
    {
        var exceptions = new ExceptionCaptureHandler();
        var channel = new EmbeddedChannel(new CRpcServerHandler(), exceptions);
        var exception = new InvalidOperationException("boom");

        channel.Pipeline.FireExceptionCaught(exception);

        Assert.Same(exception, exceptions.Exception);
    }

    private static int NextServiceId()
    {
        return Interlocked.Increment(ref nextServiceId);
    }

    private static CRpcMessage CreateRequest(int serviceId)
    {
        var header = CRpcMessageHeader.valueOf(
            CRpcMessageState.STATE_NONE,
            resultCode: 0,
            sn: 1,
            module: (short)serviceId,
            command: 1);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        return CRpcMessage.valueOf(header, Array.Empty<byte>());
    }

    private sealed class RecordingService : IRpcService
    {
        private readonly int serviceId;

        public RecordingService(int serviceId)
        {
            this.serviceId = serviceId;
        }

        public int CallCount { get; private set; }

        public int? LastThreadId { get; private set; }

        public CRpcLoop? LastLoop { get; private set; }

        public int GetServiceId()
        {
            return serviceId;
        }

        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            CallCount++;
            LastThreadId = Environment.CurrentManagedThreadId;
            LastLoop = CRpcLoop.Current;
            return CRpcTask.FromResult((0, Array.Empty<byte>()), CRpcLoop.Current);
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
}
