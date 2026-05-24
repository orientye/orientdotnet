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

    public HttpServerHandler(CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        this.loop = loop;
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest request)
    {
        if (!DotNetty.Codecs.Http.HttpMethod.Post.Equals(request.Method))
        {
            WriteJsonResponse(ctx, request, HttpResponseStatus.MethodNotAllowed, """{"error":"method not allowed"}""");
            return;
        }

        if (!IsJsonContentType(request))
        {
            WriteJsonResponse(ctx, request, HttpResponseStatus.UnsupportedMediaType, """{"error":"content type must be application/json"}""");
            return;
        }

        if (!TryParseRoute(request.Uri, out var serviceId, out var methodId))
        {
            WriteJsonResponse(ctx, request, HttpResponseStatus.NotFound, """{"error":"route not found"}""");
            return;
        }

        var jsonBody = request.Content.ToString(Encoding.UTF8);
        if (loop.IsInLoopThread)
        {
            ProcessOnLoop(ctx, request, serviceId, methodId, jsonBody);
            return;
        }

        loop.Post(() => ProcessOnLoop(ctx, request, serviceId, methodId, jsonBody));
    }

    private void ProcessOnLoop(
        IChannelHandlerContext ctx,
        IFullHttpRequest httpRequest,
        ushort serviceId,
        ushort methodId,
        string jsonBody)
    {
        try
        {
            if (!loop.TryGetService(serviceId, out var service))
            {
                WriteJsonResponse(ctx, httpRequest, HttpResponseStatus.NotFound, """{"error":"service not found"}""");
                return;
            }

            if (service is not IRpcHttpJsonCodec codec
                || !codec.TryGetHttpMethodParsers(methodId, out var requestParser, out var responseParser))
            {
                WriteJsonResponse(ctx, httpRequest, HttpResponseStatus.NotFound, """{"error":"method not found"}""");
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
                WriteJsonResponse(ctx, httpRequest, HttpResponseStatus.BadRequest, """{"error":"invalid json body"}""");
                return;
            }

            var sn = Interlocked.Increment(ref nextSequence);
            var rpcRequest = CreateRpcRequest(serviceId, methodId, requestBytes, sn);
            var invokeTask = RpcServiceInvoker.InvokeAsync(service, new CRpcContext(), rpcRequest);
            var awaiter = invokeTask.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                CompleteInvoke(awaiter, ctx, httpRequest, responseParser);
                return;
            }

            awaiter.OnCompleted(() => CompleteInvoke(awaiter, ctx, httpRequest, responseParser));
        }
        catch (Exception exception)
        {
            WriteJsonResponse(ctx, httpRequest, HttpResponseStatus.InternalServerError, """{"error":"internal server error"}""");
            Console.Error.WriteLine($"HttpServerHandler: {exception}");
        }
    }

    private static void CompleteInvoke(
        CRpcTask<(int code, byte[] body)>.Awaiter awaiter,
        IChannelHandlerContext ctx,
        IFullHttpRequest httpRequest,
        MessageParser responseParser)
    {
        try
        {
            var (code, responseBytes) = awaiter.GetResult();
            WriteOkResponse(ctx, httpRequest, responseParser, code, responseBytes);
        }
        catch (Exception exception)
        {
            WriteJsonResponse(ctx, httpRequest, HttpResponseStatus.InternalServerError, """{"error":"internal server error"}""");
            Console.Error.WriteLine($"HttpServerHandler: {exception}");
        }
    }

    private static void WriteOkResponse(
        IChannelHandlerContext ctx,
        IFullHttpRequest httpRequest,
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
        WriteJsonResponse(ctx, httpRequest, HttpResponseStatus.OK, payload);
    }

    private static CRpcMessage CreateRpcRequest(ushort serviceId, ushort methodId, byte[] body, long sn)
    {
        var header = CRpcMessageHeader.valueOf(CRpcMessageState.STATE_NONE, 0, sn, serviceId, methodId);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        return CRpcMessage.valueOf(header, body);
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
        IFullHttpRequest request,
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

        if (status.Code != 200)
        {
            ReferenceCountUtil.Release(request);
        }

        var keepAlive = HttpUtil.IsKeepAlive(request);
        if (keepAlive && status.Code == 200)
        {
            response.Headers.Set(HttpHeaderNames.Connection, HttpHeaderValues.KeepAlive);
        }

        _ = ctx.WriteAndFlushAsync(response);
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
