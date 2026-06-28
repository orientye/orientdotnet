using System.Net.Sockets;
using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Protocol;
using Orient.Rpc.Util;
using Orient.Rpc.Codec;
using Orient.Rpc.Server;
using DotNetty.Transport.Channels;

namespace GateWay;

public class GateWayServerHandler : ChannelHandlerAdapter
{
    private readonly CRpcServer server;
    private readonly GateWaySessionTable sessions;
    private readonly ushort fallbackServiceId;

    public GateWayServerHandler(CRpcServer server, GateWaySessionTable sessions, ushort fallbackServiceId = 0)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
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

        if (message.MessageType == CRpcMessageType.Heartbeat)
        {
            return;
        }

        server.Loop.Post(() =>
        {
            if (server.Services.TryGet(message.ServiceId, out var rpcService))
            {
                ProcessMessage(rpcService, ctx, message);
            }
            else if (server.Services.TryGet(fallbackServiceId, out var fallbackService))
            {
                ProcessMessage(fallbackService, ctx, message);
            }
            else
            {
                GateWayResponseUtil.WriteErrorResponse(ctx, message);
            }
        });
    }

    private void ProcessMessage(IRpcService rpcService, IChannelHandlerContext ctx, object msg)
    {
        if (!server.Connections.TryGetByChannel(ctx.Channel, out var connection))
        {
            GateWayResponseUtil.WriteErrorResponse(ctx, (CRpcMessage)msg, (int)CRpcStatusCode.Unavailable);
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

    private static async OrientTask ProcessMessageAsync(
        IRpcService rpcService,
        CRpcConnection connection,
        IChannelHandlerContext ctx,
        object msg)
    {
        var rpcContext = new CRpcContext(connection);
        var request = (CRpcMessage)msg;
        var (resultCode, bytes) = await rpcService.OnMessageAsync(rpcContext, request);
        var rsp = request.CreateResponse(resultCode, bytes);
        ChannelWriteUtil.WriteAndFlushFireAndForget(ctx, rsp);
    }

    private static void CompleteProcessMessage(OrientTask.Awaiter awaiter)
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
        server.Loop.Post(() =>
        {
            long? connectionId = null;
            if (server.Connections.TryGetByChannel(context.Channel, out var connection))
            {
                connectionId = connection.ConnectionId;
            }

            server.Connections.Unregister(context.Channel);

            if (connectionId is not null)
            {
                var removeTask = sessions.RemoveAsync(connectionId.Value);
                var removeAwaiter = removeTask.GetAwaiter();
                if (!removeAwaiter.IsCompleted)
                {
                    removeAwaiter.OnCompleted(() =>
                    {
                        try
                        {
                            removeAwaiter.GetResult();
                        }
                        catch (Exception exception)
                        {
                            Console.WriteLine($"GateWay session remove exception={exception}");
                        }
                    });
                }
            }
        });

        context.FireChannelInactive();
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        if (IsRemoteDisconnect(exception))
        {
            return;
        }

        context.FireExceptionCaught(exception);
    }

    private static bool IsRemoteDisconnect(Exception exception)
    {
        return exception is SocketException { SocketErrorCode: SocketError.ConnectionReset }
            || exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset };
    }
}
