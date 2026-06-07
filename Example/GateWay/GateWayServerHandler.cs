using System.Net.Sockets;
using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Transport.Channels;

namespace GateWay;

public class GateWayServerHandler : ChannelHandlerAdapter
{
    private readonly CRpcServer server;
    private readonly ushort fallbackServiceId;

    public GateWayServerHandler(CRpcServer server, ushort fallbackServiceId = 0)
    {
        ArgumentNullException.ThrowIfNull(server);
        this.server = server;
        this.fallbackServiceId = fallbackServiceId;
    }

    public override void ChannelActive(IChannelHandlerContext context)
    {
        server.Loop.Post(() => server.Connections.Register(context.Channel));
        base.ChannelActive(context);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        var message = (CRpcMessage)msg;

        var serviceId = message.getServiceId();
        var methodId = message.getMethodId();
        server.Loop.Post(() =>
        {
            if (server.Loop.TryGetService(serviceId, out var rpcService))
            {
                ProcessMessage(rpcService, ctx, message);
            }
            else if (server.Loop.TryGetService(fallbackServiceId, out var fallbackService))
            {
                // Route unknown serviceIds through the Gateway fallback
                ProcessMessage(fallbackService, ctx, message);
            }
            else
            {
                Console.WriteLine($"GateWay no service or fallback for serviceId={serviceId}");
            }
        });

        Console.WriteLine($"GateWayServerHandler recv msg: serviceId={serviceId}, methodId={methodId}");
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
        var (resultCode, bytes) = await rpcService.OnMessageAsync(rpcContext, request);
        var rsp = request.createResponse(resultCode, bytes);
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
            Console.WriteLine($"GateWay process exception={exception}");
        }
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        server.Loop.Post(() => server.Connections.Unregister(context.Channel));
        Console.WriteLine($"GateWay client disconnected: {context.Channel.RemoteAddress}");
        context.FireChannelInactive();
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        if (IsRemoteDisconnect(exception))
        {
            Console.WriteLine($"GateWay remote disconnected: {exception.Message}");
            return;
        }

        Console.WriteLine($"GateWay exception={exception}");
        context.FireExceptionCaught(exception);
    }

    private static bool IsRemoteDisconnect(Exception exception)
    {
        return exception is SocketException { SocketErrorCode: SocketError.ConnectionReset }
            || exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset };
    }
}
