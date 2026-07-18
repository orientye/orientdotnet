using System.Net.Sockets;
using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Protocol;
using Orient.Rpc.Util;
using Orient.Rpc.Codec;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Server;

public class CRpcServerHandler : ChannelHandlerAdapter
{
    private readonly CRpcServer server;

    public CRpcServerHandler(CRpcServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        this.server = server;
    }

    public override void ChannelActive(IChannelHandlerContext context)
    {
        server.Executor.Post(() => server.Connections.Register(context.Channel));
        base.ChannelActive(context);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        var message = (CRpcMessage)msg;

        if (message.MessageType == CRpcMessageType.Heartbeat)
        {
            return;
        }

        if (message.MessageType != CRpcMessageType.Request)
        {
            Console.WriteLine(
                $"CRpcServerHandler ignored inbound message type {message.MessageType}: serviceId={message.ServiceId}, methodId={message.MethodId}");
            return;
        }

        var serviceId = message.ServiceId;
        var methodId = message.MethodId;
        server.Executor.Post(() =>
        {
            if (server.Services.TryGet(serviceId, out var rpcService))
            {
                ProcessMessage(rpcService, ctx, message);
            }
            else
            {
                RpcServiceInvoker.WriteFrameworkErrorResponse(ctx, message, CRpcStatusCode.ServiceNotFound);
            }
        });
        
        Console.WriteLine($"CRpcServerHandler recv msg: serviceId={serviceId}, methodId={methodId}");

    }

    private void ProcessMessage(IRpcService rpcService, IChannelHandlerContext ctx, object msg)
    {
        var request = (CRpcMessage)msg;
        if (!server.Connections.TryGetByChannel(ctx.Channel, out var connection))
        {
            RpcServiceInvoker.WriteFrameworkErrorResponse(ctx, request, CRpcStatusCode.Unavailable);
            return;
        }

        var task = ProcessMessageAsync(rpcService, connection, ctx, request);
        var awaiter = task.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            CompleteProcessMessage(ctx, request, awaiter);
            return;
        }

        awaiter.OnCompleted(() => CompleteProcessMessage(ctx, request, awaiter));
    }

    private static async OrientTask ProcessMessageAsync(
        IRpcService rpcService,
        CRpcConnection connection,
        IChannelHandlerContext ctx,
        CRpcMessage request)
    {
        try
        {
            var rpcContext = new CRpcContext(connection);
            var (resultCode, bytes) = await RpcServiceInvoker.InvokeAsync(rpcService, rpcContext, request);
            var rsp = RpcServiceInvoker.BuildCrpcResponse(request, resultCode, bytes);
            ChannelWriteUtil.WriteAndFlushFireAndForget(ctx, rsp);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"CRpcServerHandler process exception: serviceId={request.ServiceId}, methodId={request.MethodId}, exception={exception}");
            RpcServiceInvoker.WriteFrameworkErrorResponse(ctx, request, CRpcStatusCode.InternalError);
        }
    }

    private static void CompleteProcessMessage(
        IChannelHandlerContext ctx,
        CRpcMessage request,
        OrientTask.Awaiter awaiter)
    {
        try
        {
            awaiter.GetResult();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"CRpcServerHandler process exception: serviceId={request.ServiceId}, methodId={request.MethodId}, exception={exception}");
            RpcServiceInvoker.WriteFrameworkErrorResponse(ctx, request, CRpcStatusCode.InternalError);
        }
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        server.Executor.Post(() => server.Connections.Unregister(context.Channel));
        Console.WriteLine($"CRpcServerHandler client disconnected: {context.Channel.RemoteAddress}");
        context.FireChannelInactive();
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        if (IsRemoteDisconnect(exception))
        {
            Console.WriteLine($"CRpcServerHandler remote disconnected: {exception.Message}");
            return;
        }

        Console.WriteLine($"******************exception={exception}");
        context.FireExceptionCaught(exception);
    }

    private static bool IsRemoteDisconnect(Exception exception)
    {
        return exception is SocketException { SocketErrorCode: SocketError.ConnectionReset }
            || exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset };
    }
}
