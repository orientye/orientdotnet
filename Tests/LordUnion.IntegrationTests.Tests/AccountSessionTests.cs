using CRpc.Async;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Tests;

public class AccountSessionTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();

    [Fact]
    public void SetState_UpdatesStateAndPhaseOnLoopThread()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.Connected);
            Assert.Equal(AccountSessionState.Connected, session.State);
            Assert.Equal(ProtocolPhase.AnonymousBrowse, session.CurrentPhase);

            session.SetState(AccountSessionState.LoggedIn);
            Assert.Equal(AccountSessionState.LoggedIn, session.State);
            Assert.Equal(ProtocolPhase.Login, session.CurrentPhase);

            await CRpcTask.CompletedTask(loop);
        });
    }

    [Fact]
    public void SendRequestAsync_LogsAndCapturesEncodedPacket()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var request = codec.CreateAnonymousBrowseRequest(serialId: 42);

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.Connected);
            await session.SendRequestAsync(request);

            Assert.NotNull(session.LastSentPacket);
            Assert.Single(session.SentPackets);
            Assert.Single(session.SentMessages);

            var logEntry = session.SentMessages[0];
            Assert.Equal("player1", logEntry.AccountAlias);
            Assert.Equal(SessionMessageDirection.Sent, logEntry.Direction);
            Assert.Equal(ProtocolMessageKind.AnonymousBrowseReq, logEntry.Kind);
            Assert.Equal(AccountSessionState.Connected, logEntry.State);
            Assert.Equal(ProtocolPhase.AnonymousBrowse, logEntry.Phase);
            Assert.Equal(ServerPacketFrame.ClientSendHeaderMagic, logEntry.Header0);

            var decoded = codec.DecodePacket(
                session.LastSentPacket!,
                new ProtocolDecodeContext
                {
                    AccountAlias = session.Alias,
                    Phase = session.CurrentPhase,
                });

            Assert.Equal(ProtocolMessageKind.AnonymousBrowseReq, decoded.Kind);
            Assert.Equal(42u, decoded.AnonymousBrowseRequest!.Serialid);
        });
    }

    [Fact]
    public void WaitForMessageAsync_CompletesWhenMatchingAckDelivered()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player2", codec);
        var ack = CreateAnonymousBrowseAck(1001);

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.Connected);

            var waitTask = session.WaitForMessageAsync(ProtocolMessageKind.AnonymousBrowseAck, timeoutMs: 5000);
            session.DeliverIncomingMessage(ack);

            var received = await waitTask;
            Assert.Equal(ProtocolMessageKind.AnonymousBrowseAck, received.Kind);
            Assert.Equal(1001u, received.Header0);
        });
    }

    [Fact]
    public void WaitForMessageAsync_TimesOutWithDescriptiveException()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player3", codec);

        var exception = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.Connected);

            try
            {
                await session.WaitForMessageAsync(ProtocolMessageKind.CommonLoginAck, timeoutMs: 50);
                throw new InvalidOperationException("Expected timeout.");
            }
            catch (TimeoutException timeoutException)
            {
                return timeoutException;
            }
        });

        Assert.Contains("CommonLoginAck", exception.Message, StringComparison.Ordinal);
        Assert.Contains("player3", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Connected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitForMessageAsync_RejectsSecondConcurrentWait()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.Connected);
            _ = session.WaitForMessageAsync(ProtocolMessageKind.AnonymousBrowseAck, timeoutMs: 5000);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                session.WaitForMessageAsync(ProtocolMessageKind.CommonLoginAck, timeoutMs: 5000));

            Assert.Contains("already has an active wait", exception.Message, StringComparison.Ordinal);
            Assert.Contains("AnonymousBrowseAck", exception.Message, StringComparison.Ordinal);

            await CRpcTask.CompletedTask(loop);
        });
    }

    [Theory]
    [InlineData(ProtocolMessageKind.AnonymousBrowseAck, SessionMessageRouteArea.Lobby)]
    [InlineData(ProtocolMessageKind.CommonLoginReq, SessionMessageRouteArea.Lobby)]
    [InlineData(ProtocolMessageKind.TourneySignupAck, SessionMessageRouteArea.Lobby)]
    [InlineData(ProtocolMessageKind.EnterMatchAck, SessionMessageRouteArea.Match)]
    [InlineData(ProtocolMessageKind.EnterRoundReq, SessionMessageRouteArea.Match)]
    [InlineData(ProtocolMessageKind.LordAck, SessionMessageRouteArea.LordUnion)]
    [InlineData(ProtocolMessageKind.LordReq, SessionMessageRouteArea.LordUnion)]
    [InlineData(ProtocolMessageKind.Unknown, SessionMessageRouteArea.Unknown)]
    public void SessionMessageRouter_ClassifiesMessagesByArea(
        ProtocolMessageKind kind,
        SessionMessageRouteArea expectedArea)
    {
        var message = new ProtocolMessage { Kind = kind, Header0 = 2001 };
        Assert.Equal(expectedArea, SessionMessageRouter.GetRouteArea(message));
    }

    [Fact]
    public void DeliverIncomingMessage_AddsReceiveLogEntry()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player2", codec);
        var ack = CreateAnonymousBrowseAck(2002);

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.Connected);
            session.DeliverIncomingMessage(ack);

            await CRpcTask.Delay(1, loop);

            Assert.Single(session.ReceivedMessages);
            var logEntry = session.ReceivedMessages[0];
            Assert.Equal("player2", logEntry.AccountAlias);
            Assert.Equal(SessionMessageDirection.Received, logEntry.Direction);
            Assert.Equal(ProtocolMessageKind.AnonymousBrowseAck, logEntry.Kind);
            Assert.Equal(2002u, logEntry.Header0);
            Assert.Equal(AccountSessionState.Connected, logEntry.State);
            Assert.True(logEntry.Timestamp <= DateTimeOffset.UtcNow);
        });
    }

    private static ProtocolMessage CreateAnonymousBrowseAck(uint header0)
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
                    },
                },
            },
        };
    }
}
