using System.Text;
using System.Text.RegularExpressions;

using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using Google.Protobuf;

namespace CRpc.Rpc.CRpc.Server;

public sealed class HttpServerHandler : SimpleChannelInboundHandler<IFullHttpRequest>
{
    private static readonly Regex RoutePattern = new(
        @"^/(\d+)/(\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static long nextSequence;

    private readonly CRpcLoop loop;
    private readonly CRpcConnectionRegistry connections;

    public HttpServerHandler(CRpcLoop loop, CRpcConnectionRegistry connections)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(connections);
        this.loop = loop;
        this.connections = connections;
    }

    public override void ChannelActive(IChannelHandlerContext context)
    {
        if (loop.IsInLoopThread)
        {
            connections.Register(context.Channel);
        }
        else
        {
            loop.Post(() => connections.Register(context.Channel));
        }

        base.ChannelActive(context);
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        if (loop.IsInLoopThread)
        {
            connections.Unregister(context.Channel);
        }
        else
        {
            loop.Post(() => connections.Unregister(context.Channel));
        }

        base.ChannelInactive(context);
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest request)
    {
        var keepAlive = HttpUtil.IsKeepAlive(request);

        if (!DotNetty.Codecs.Http.HttpMethod.Post.Equals(request.Method))
        {
            WriteJsonResponse(ctx, keepAlive, HttpResponseStatus.MethodNotAllowed, """{"error":"method not allowed"}""");
            return;
        }

        if (!IsJsonContentType(request))
        {
            WriteJsonResponse(ctx, keepAlive, HttpResponseStatus.UnsupportedMediaType, """{"error":"content type must be application/json"}""");
            return;
        }

        if (!TryParseRoute(request.Uri, out var serviceId, out var methodId))
        {
            WriteJsonResponse(ctx, keepAlive, HttpResponseStatus.NotFound, """{"error":"route not found"}""");
            return;
        }

        var jsonBody = request.Content.ToString(Encoding.UTF8);
        if (loop.IsInLoopThread)
        {
            ProcessOnLoop(ctx, keepAlive, serviceId, methodId, jsonBody);
            return;
        }

        loop.Post(() => ProcessOnLoop(ctx, keepAlive, serviceId, methodId, jsonBody));
    }

    private void ProcessOnLoop(
        IChannelHandlerContext ctx,
        bool keepAlive,
        ushort serviceId,
        ushort methodId,
        string jsonBody)
    {
        try
        {
            if (!loop.TryGetService(serviceId, out var service))
            {
                WriteJsonResponse(ctx, keepAlive, HttpResponseStatus.NotFound, """{"error":"service not found"}""");
                return;
            }

            if (service is not IRpcHttpJsonCodec codec
                || !codec.TryGetHttpMethodParsers(methodId, out var requestParser, out var responseParser))
            {
                WriteJsonResponse(ctx, keepAlive, HttpResponseStatus.NotFound, """{"error":"method not found"}""");
                return;
            }

            byte[] requestBytes;
            try
            {
                var message = requestParser.ParseJson(jsonBody);
                requestBytes = message.ToByteArray();
            }
            catch (Exception)
            {
                WriteJsonResponse(ctx, keepAlive, HttpResponseStatus.BadRequest, """{"error":"invalid json body"}""");
                return;
            }

            var sn = Interlocked.Increment(ref nextSequence);
            var rpcRequest = CreateRpcRequest(serviceId, methodId, requestBytes, sn);
            if (!connections.TryGetByChannel(ctx.Channel, out var connection))
            {
                WriteJsonResponse(ctx, keepAlive, HttpResponseStatus.ServiceUnavailable, """{"error":"connection not ready"}""");
                return;
            }

            var invokeTask = RpcServiceInvoker.InvokeAsync(service, new CRpcContext(connection), rpcRequest);
            var awaiter = invokeTask.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                CompleteInvoke(awaiter, ctx, keepAlive, responseParser);
                return;
            }

            awaiter.OnCompleted(() => CompleteInvoke(awaiter, ctx, keepAlive, responseParser));
        }
        catch (Exception exception)
        {
            WriteJsonResponse(ctx, keepAlive, HttpResponseStatus.InternalServerError, """{"error":"internal server error"}""");
            Console.Error.WriteLine($"HttpServerHandler: {exception}");
        }
    }

    private static void CompleteInvoke(
        CRpcTask<(int code, byte[] body)>.Awaiter awaiter,
        IChannelHandlerContext ctx,
        bool keepAlive,
        MessageParser responseParser)
    {
        try
        {
            var (code, responseBytes) = awaiter.GetResult();
            WriteOkResponse(ctx, keepAlive, responseParser, code, responseBytes);
        }
        catch (Exception exception)
        {
            WriteJsonResponse(ctx, keepAlive, HttpResponseStatus.InternalServerError, """{"error":"internal server error"}""");
            Console.Error.WriteLine($"HttpServerHandler: {exception}");
        }
    }

    private static void WriteOkResponse(
        IChannelHandlerContext ctx,
        bool keepAlive,
        MessageParser responseParser,
        int code,
        byte[] responseBytes)
    {
        string replyJson;
        try
        {
            var replyMessage = responseParser.ParseFrom(responseBytes);
            replyJson = JsonFormatter.Default.Format(replyMessage);
        }
        catch (Exception)
        {
            replyJson = "{}";
        }

        var payload = $"{{\"code\":{code},\"body\":{replyJson}}}";
        WriteJsonResponse(ctx, keepAlive, HttpResponseStatus.OK, payload);
    }

    private static CRpcMessage CreateRpcRequest(ushort serviceId, ushort methodId, byte[] body, long sn)
    {
        return CRpcMessage.Create(
            CRpcMessageType.Request,
            serviceId,
            methodId,
            sn,
            resultCode: 0,
            body);
    }

    private static bool TryParseRoute(string uri, out ushort serviceId, out ushort methodId)
    {
        serviceId = 0;
        methodId = 0;
        var path = uri;
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        var match = RoutePattern.Match(path);
        if (!match.Success)
        {
            return false;
        }

        return ushort.TryParse(match.Groups[1].Value, out serviceId)
            && ushort.TryParse(match.Groups[2].Value, out methodId);
    }

    private static bool IsJsonContentType(IFullHttpRequest request)
    {
        var contentType = request.Headers.Get(HttpHeaderNames.ContentType, AsciiString.Empty);
        return contentType.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteJsonResponse(
        IChannelHandlerContext ctx,
        bool keepAlive,
        HttpResponseStatus status,
        string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var response = new DefaultFullHttpResponse(
            DotNetty.Codecs.Http.HttpVersion.Http11,
            status,
            Unpooled.WrappedBuffer(bytes));
        response.Headers.Set(HttpHeaderNames.ContentType, "application/json; charset=utf-8");
        response.Headers.Set(HttpHeaderNames.ContentLength, bytes.Length);

        if (keepAlive && status.Code == 200)
        {
            response.Headers.Set(HttpHeaderNames.Connection, HttpHeaderValues.KeepAlive);
        }

        ChannelWriteUtil.WriteAndFlushFireAndForget(ctx, response);
        if (!keepAlive)
        {
            _ = ctx.CloseAsync();
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        Console.Error.WriteLine($"HttpServerHandler exception: {exception}");
        _ = context.CloseAsync();
    }
}
