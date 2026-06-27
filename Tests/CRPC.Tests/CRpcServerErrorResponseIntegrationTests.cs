using System.Net;
using System.Reflection;
using CRpc.Async;
using CRpc.Rpc.CRpc;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Transport.Channels;

namespace CRPC.Tests;

public class CRpcServerErrorResponseIntegrationTests : CrpcTestBase
{
    [Fact]
    public void UnknownServiceReturnsServiceNotFoundOverTcp()
    {
        var loop = new CRpcLoop();
        var server = new CRpcServer(loop, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0,
        });

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await server.StartAsync();
            var port = GetBoundPort(server);
            var client = new CRpcClient(loop);
            await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

            try
            {
                var response = await client.CallAsync(
                    serviceId: 9999,
                    methodId: 1,
                    body: Array.Empty<byte>(),
                    timeout: 5000);

                Assert.Equal(CRpcMessageType.Response, response.MessageType);
                Assert.Equal((int)CRpcStatusCode.ServiceNotFound, response.ResultCode);
                Assert.Empty(response.Body);
            }
            finally
            {
                await client.CloseAsync();
                await client.ShutdownIoAsync();
                await server.StopAsync();
            }
        });
    }

    private static int GetBoundPort(CRpcServer server)
    {
        var field = typeof(CRpcServer).GetField("bootstrapChannel", BindingFlags.Instance | BindingFlags.NonPublic);
        var channel = (IChannel?)field!.GetValue(server);
        var endpoint = (IPEndPoint)channel!.LocalAddress;
        return endpoint.Port;
    }
}
