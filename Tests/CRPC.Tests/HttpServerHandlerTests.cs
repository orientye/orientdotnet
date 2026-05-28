using System.Text;
using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Channels.Embedded;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace CRPC.Tests;

public class HttpServerHandlerTests : CrpcTestBase
{
    [Fact]
    public void PostJsonInvokesServiceAndReturnsEnvelope()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var service = new TestJsonEchoService();
        loop.RegisterService(service);

        var connections = new CRpcConnectionRegistry(loop);
        var channel = new EmbeddedChannel(new HttpServerHandler(loop, connections));
        channel.Pipeline.FireChannelActive();

        var request = new DefaultFullHttpRequest(
            DotNetty.Codecs.Http.HttpVersion.Http11,
            DotNetty.Codecs.Http.HttpMethod.Post,
            "/1000/1");
        request.Headers.Set(HttpHeaderNames.ContentType, "application/json; charset=utf-8");
        request.Content.WriteBytes(Encoding.UTF8.GetBytes("{}"));

        Assert.False(channel.WriteInbound(request));

        loop.Tick();

        var response = ReadHttpResponse(channel);
        Assert.NotNull(response);
        Assert.Equal(200, response.Status.Code);
        var json = response.Content.ToString(Encoding.UTF8);
        Assert.Contains("\"code\":0", json);
        Assert.Contains("\"body\":", json);
    }

    [Fact]
    public void WrongContentTypeReturns415()
    {
        var loop = new CRpcLoop();
        var connections = new CRpcConnectionRegistry(loop);
        var channel = new EmbeddedChannel(new HttpServerHandler(loop, connections));
        channel.Pipeline.FireChannelActive();

        var request = new DefaultFullHttpRequest(
            DotNetty.Codecs.Http.HttpVersion.Http11,
            DotNetty.Codecs.Http.HttpMethod.Post,
            "/1000/1");
        request.Headers.Set(HttpHeaderNames.ContentType, "text/plain");
        request.Content.WriteBytes(Encoding.UTF8.GetBytes("hello"));

        Assert.False(channel.WriteInbound(request));

        var response = ReadHttpResponse(channel);
        Assert.NotNull(response);
        Assert.Equal(415, response.Status.Code);
    }

    private static IFullHttpResponse? ReadHttpResponse(EmbeddedChannel channel)
    {
        var message = channel.ReadOutbound<object>();
        return message as IFullHttpResponse;
    }

    private static void DrainLoop(CRpcLoop loop, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            loop.Tick();
        }
    }

    private sealed class TestJsonEchoService : IRpcService, IRpcHttpJsonCodec
    {
        public ushort GetServiceId() => 1000;

        public bool TryGetHttpMethodParsers(
            ushort methodId,
            out MessageParser requestParser,
            out MessageParser responseParser)
        {
            requestParser = null!;
            responseParser = null!;
            if (methodId != 1)
            {
                return false;
            }

            requestParser = Empty.Parser;
            responseParser = Empty.Parser;
            return true;
        }

        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            _ = Empty.Parser.ParseFrom(((CRpcMessage)req).getBody());
            return CRpcTask.FromResult((0, Array.Empty<byte>()), CRpcLoop.Current);
        }
    }
}
