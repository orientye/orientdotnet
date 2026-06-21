using System.Net.Sockets;
using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Server;

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
        server.Loop.Post(() => server.Connections.Register(context.Channel));
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
        server.Loop.Post(() =>
        {
            if (server.Loop.TryGetService(serviceId, out var rpcService))
            {
                ProcessMessage(rpcService, ctx, message);
            }
        });
        
        Console.WriteLine($"CRpcServerHandler recv msg: serviceId={serviceId}, methodId={methodId}");

    }

    private void ProcessMessage(IRpcService rpcService, IChannelHandlerContext ctx, object msg)
    {
        if (!server.Connections.TryGetByChannel(ctx.Channel, out var connection))
        {
            return;
        }

        var task = ProcessMessageAsync(rpcService, connection, ctx, msg);
        var awaiter = task.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            CompleteProcessMessage(awaiter);
            return;
        }

        awaiter.OnCompleted(() => CompleteProcessMessage(awaiter));
    }

    private static async CRpcTask ProcessMessageAsync(
        IRpcService rpcService,
        CRpcConnection connection,
        IChannelHandlerContext ctx,
        object msg)
    {
        var rpcContext = new CRpcContext(connection);
        var request = (CRpcMessage)msg;
        var (resultCode, bytes) = await RpcServiceInvoker.InvokeAsync(rpcService, rpcContext, request);
        var rsp = RpcServiceInvoker.BuildCrpcResponse(request, resultCode, bytes);
        ChannelWriteUtil.WriteAndFlushFireAndForget(ctx, rsp);
    }

    private static void CompleteProcessMessage(CRpcTask.Awaiter awaiter)
    {
        try
        {
            awaiter.GetResult();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"******************process exception={exception}");
        }
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        server.Loop.Post(() => server.Connections.Unregister(context.Channel));
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
