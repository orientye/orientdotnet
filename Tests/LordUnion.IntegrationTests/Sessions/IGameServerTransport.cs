using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Sessions;

public interface IGameServerTransport
{
    CRpcTask ConnectAsync(
        ServerConfig server,
        TimeSpan timeout,
        CRpcLoop loop,
        CancellationToken cancellationToken = default);

    CRpcTask SendAsync(byte[] packet, CRpcLoop loop);

    CRpcTask DisconnectAsync(CRpcLoop loop);

    void BindIncomingHandler(AccountSession session, ServerProtocolCodec codec);
}