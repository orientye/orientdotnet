using System.Net;
using System.Reflection;
using Orient.Runtime;
using Orient.Rpc.Protocol;
using Orient.Rpc.Client;
using Orient.Rpc.Codec;
using Orient.Rpc.Server;
using DotNetty.Transport.Channels;

namespace Orient.Tests;

public class CRpcServerErrorResponseIntegrationTests : OrientTestBase
{
    [Fact]
    public void UnknownServiceReturnsServiceNotFoundOverTcp()
    {
        var executor = new OrientExecutor();
        var server = new CRpcServer(executor, new CRpcServerOptions
        {
            Address = IPAddress.Loopback,
            Port = 0,
        });

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
        {
            await server.StartAsync();
            var port = GetBoundPort(server);
            var client = new CRpcClient(executor);
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
