using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

public class LoginFlowTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();

    [Fact]
    public void RunAsync_SucceedsWithUserInfoAndParamZero()
    {
        const uint userId = 214291552;
        const uint anonymousRouteId = 1001;
        const uint loginRouteId = 1002;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = CreateAutoResponder(
            session,
            browseAck: CreateAnonymousBrowseAck(anonymousRouteId, aesKey: "test-aes-key"),
            loginAck: CreateCommonLoginAck(loginRouteId, param: 0, userId: userId, nickname: "Tester"));

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            var flow = new LoginFlow(codec);
            return await flow.RunAsync(
                session,
                CreateAccount(),
                CreateServer(),
                CreateProtocol(),
                TimeSpan.FromSeconds(5),
                transport);
        });

        Assert.True(result.Success);
        Assert.Equal(userId, result.UserId);
        Assert.Equal("Tester", result.Nickname);
        Assert.Equal("test-aes-key", result.AesKey);
        Assert.Equal(anonymousRouteId, result.AnonymousRouteId);
        Assert.Equal(loginRouteId, result.LoginRouteId);
        Assert.Equal(AccountSessionState.LoggedIn, session.State);
        Assert.Equal(userId, session.UserId);
        Assert.Equal("test-aes-key", session.AesKey);
        Assert.Equal(2, transport.SentPackets.Count);
    }

    [Fact]
    public void RunAsync_SucceedsWithParam31WhenSessionIdPresent()
    {
        const uint userId = 214291552;
        const ulong sessionId = 199213504010403840;
        const string loginJson =
            "{\"G_SessionID\":199213504010403840,\"pid\":214291552,\"reg_flag\":0}";

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = CreateAutoResponder(
            session,
            browseAck: CreateAnonymousBrowseAck(1001),
            loginAck: CreateCommonLoginAck(1002, param: 31, json: loginJson));

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            var flow = new LoginFlow(codec);
            return await flow.RunAsync(
                session,
                CreateAccount(),
                CreateServer(),
                CreateProtocol(),
                TimeSpan.FromSeconds(5),
                transport);
        });

        Assert.True(result.Success);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(31u, result.LoginErrorCode);
        Assert.Equal(AccountSessionState.LoggedIn, session.State);
        Assert.Equal(sessionId, session.SessionId);
    }

    [Fact]
    public void RunAsync_ReturnsFailureWhenServerReturnsErrorCode()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player2", codec);
        var transport = CreateAutoResponder(
            session,
            browseAck: CreateAnonymousBrowseAck(1001),
            loginAck: CreateCommonLoginAck(1002, param: 99, userId: 12345));

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            var flow = new LoginFlow(codec);
            return await flow.RunAsync(
                session,
                CreateAccount(),
                CreateServer(),
                CreateProtocol(),
                TimeSpan.FromSeconds(5),
                transport);
        });

        Assert.False(result.Success);
        Assert.Equal(99u, result.LoginErrorCode);
        Assert.Contains("error code 99", result.FailureMessage, StringComparison.Ordinal);
        Assert.Equal(AccountSessionState.Failed, session.State);
    }

    [Fact]
    public void RunAsync_TimesOutWhenLoginAckMissing()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player3", codec);
        var transport = new FakeGameServerTransport();
        transport.BindIncomingHandler(session, codec);
        transport.OnPacketSentAsync = async (packet, packetLoop) =>
        {
            var decoded = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });
            if (decoded.Kind == ProtocolMessageKind.AnonymousBrowseReq)
            {
                transport.DeliverIncomingMessage(CreateAnonymousBrowseAck(1001));
            }

            await CRpcTask.CompletedTask(packetLoop);
        };

        var exception = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            var flow = new LoginFlow(codec);
            try
            {
                await flow.RunAsync(
                    session,
                    CreateAccount(),
                    CreateServer(),
                    CreateProtocol(),
                    TimeSpan.FromMilliseconds(50),
                    transport);
                throw new InvalidOperationException("Expected timeout.");
            }
            catch (TimeoutException timeoutException)
            {
                return timeoutException;
            }
        });

        Assert.Contains("CommonLoginAck", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AccountSessionState.Failed, session.State);
    }

    [Fact]
    public void RunAsync_ReturnsFailureWhenUserIdMissing()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = CreateAutoResponder(
            session,
            browseAck: CreateAnonymousBrowseAck(1001),
            loginAck: CreateCommonLoginAck(1002, param: 0, userId: 0));

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            var flow = new LoginFlow(codec);
            return await flow.RunAsync(
                session,
                CreateAccount(),
                CreateServer(),
                CreateProtocol(),
                TimeSpan.FromSeconds(5),
                transport);
        });

        Assert.False(result.Success);
        Assert.Null(result.UserId);
        Assert.Contains("no userid", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AccountSessionState.Failed, session.State);
    }

    private static FakeGameServerTransport CreateAutoResponder(
        AccountSession session,
        ProtocolMessage browseAck,
        ProtocolMessage loginAck)
    {
        var transport = new FakeGameServerTransport();
        transport.BindIncomingHandler(session, codec: new ServerProtocolCodec());
        transport.OnPacketSentAsync = async (packet, packetLoop) =>
        {
            var decoded = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });
            switch (decoded.Kind)
            {
                case ProtocolMessageKind.AnonymousBrowseReq:
                    transport.DeliverIncomingMessage(browseAck);
                    break;
                case ProtocolMessageKind.CommonLoginReq:
                    transport.DeliverIncomingMessage(loginAck);
                    break;
            }

            await CRpcTask.CompletedTask(packetLoop);
        };

        return transport;
    }

    private static AccountConfig CreateAccount() =>
        new() { Alias = "player1", Username = "TJJ006628", Password = "3YXRQW" };

    private static ServerConfig CreateServer() =>
        new() { Host = "127.0.0.1", Port = 30301 };

    private static ProtocolConfig CreateProtocol() =>
        new() { AppId = 2, AnonymousSerialId = 2000, LoginType = 2 };

    private static ProtocolMessage CreateAnonymousBrowseAck(uint header0, string? aesKey = null)
    {
        return new ProtocolMessage
        {
            Header0 = header0,
            Kind = ProtocolMessageKind.AnonymousBrowseAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    AnonymousAckMsg = new AnonymousBrowseAck
                    {
                        Anonymousid = 7,
                        U64servertime = 1_779_693_696_815,
                        Param = aesKey,
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateCommonLoginAck(
        uint header0,
        uint param,
        uint userId = 0,
        string nickname = "",
        string? json = null)
    {
        return new ProtocolMessage
        {
            Header0 = header0,
            Kind = ProtocolMessageKind.CommonLoginAck,
            Param = param,
            Acknowledgement = new TKMobileAckMsg
            {
                Param = param,
                LobbyAckMsg = new LobbyAckMsg
                {
                    CommonloginAckMsg = new CommonLoginAck
                    {
                        Userinfo = userId > 0
                            ? new LcUserInfoEx { Userid = userId, Nickname = nickname }
                            : null,
                        Jsondata = json,
                    },
                },
            },
        };
    }
}
