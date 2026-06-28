using System.Text;
using Orient.Runtime;
using Orient.Rpc.Server;
using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using Google.Protobuf;

namespace Example.Http;

public sealed class GreeterHttpHandler : SimpleChannelInboundHandler<IFullHttpRequest>
{
    private const string SayHelloPath = "/api/greeter/say-hello";

    private readonly OrientLoop loop;
    private readonly CRpcConnectionRegistry connections;
    private readonly HelloworldServiceImpl greeter;

    public GreeterHttpHandler(OrientLoop loop, CRpcConnectionRegistry connections, HelloworldServiceImpl greeter)
    {
        this.loop = loop;
        this.connections = connections;
        this.greeter = greeter;
    }

    public override void ChannelActive(IChannelHandlerContext context)
    {
        loop.Post(() => connections.Register(context.Channel));
        base.ChannelActive(context);
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        loop.Post(() => connections.Unregister(context.Channel));
        base.ChannelInactive(context);
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest request)
    {
        var keepAlive = HttpUtil.IsKeepAlive(request);

        if (!DotNetty.Codecs.Http.HttpMethod.Post.Equals(request.Method))
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.MethodNotAllowed, """{"error":"method not allowed"}""");
            return;
        }

        if (!string.Equals(NormalizePath(request.Uri), SayHelloPath, StringComparison.Ordinal))
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.NotFound, """{"error":"route not found"}""");
            return;
        }

        if (!IsJsonContentType(request))
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.UnsupportedMediaType, """{"error":"content type must be application/json"}""");
            return;
        }

        HelloRequest helloRequest;
        try
        {
            var json = request.Content.ToString(Encoding.UTF8);
            helloRequest = HelloRequest.Parser.ParseJson(json);
        }
        catch (Exception)
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.BadRequest, """{"error":"invalid json body"}""");
            return;
        }

        loop.Post(() => ProcessOnLoop(ctx, keepAlive, helloRequest));
    }

    private void ProcessOnLoop(IChannelHandlerContext ctx, bool keepAlive, HelloRequest helloRequest)
    {
        if (!connections.TryGetByChannel(ctx.Channel, out var connection))
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.ServiceUnavailable, """{"error":"connection not ready"}""");
            return;
        }

        var rpcContext = new CRpcContext(connection);
        var task = greeter.InvokeSayHelloAsync(rpcContext, helloRequest);
        var awaiter = task.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            Complete(ctx, keepAlive, awaiter);
            return;
        }

        awaiter.OnCompleted(() => Complete(ctx, keepAlive, awaiter));
    }

    private static void Complete(IChannelHandlerContext ctx, bool keepAlive, OrientTask<(int code, HelloReply reply)>.Awaiter awaiter)
    {
        try
        {
            var (code, reply) = awaiter.GetResult();
            var bodyJson = JsonFormatter.Default.Format(reply);
            var payload = $$"""{"code":{{code}},"body":{{bodyJson}}}""";
            WriteJson(ctx, keepAlive, HttpResponseStatus.OK, payload);
        }
        catch (Exception)
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.InternalServerError, """{"error":"internal server error"}""");
        }
    }

    private static string NormalizePath(string uri)
    {
        var path = uri;
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        return path.TrimEnd('/');
    }

    private static bool IsJsonContentType(IFullHttpRequest request)
    {
        if (!request.Headers.TryGet(HttpHeaderNames.ContentType, out var contentType))
        {
            return false;
        }

        var mediaType = contentType.ToString().Split(';')[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteJson(IChannelHandlerContext ctx, bool keepAlive, HttpResponseStatus status, string json)
    {
        var response = new DefaultFullHttpResponse(HttpVersion.Http11, status, Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(json)));
        response.Headers.Set(HttpHeaderNames.ContentType, "application/json; charset=utf-8");
        HttpUtil.SetContentLength(response, response.Content.ReadableBytes);
        if (!keepAlive)
        {
            response.Headers.Set(HttpHeaderNames.Connection, HttpHeaderValues.Close);
        }

        _ = ctx.WriteAndFlushAsync(response);
    }
}
