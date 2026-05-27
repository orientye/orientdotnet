using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Flows;

public sealed class SignupFlow
{
    private readonly ServerProtocolCodec codec;

    public SignupFlow(ServerProtocolCodec? codec = null)
    {
        this.codec = codec ?? new ServerProtocolCodec();
    }

    public CRpcTask<SignupFlowResult> RunAsync(
        AccountSession session,
        MatchConfig match,
        TimeSpan signupTimeout,
        IGameServerTransport? transport = null,
        EnterMatchFlowSessionState? matchProgressState = null)
    {
        return RunCoreAsync(session, match, signupTimeout, transport, matchProgressState);
    }

    private async CRpcTask<SignupFlowResult> RunCoreAsync(
        AccountSession session,
        MatchConfig match,
        TimeSpan signupTimeout,
        IGameServerTransport? transport,
        EnterMatchFlowSessionState? matchProgressState)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(match);
        EnsureOnLoopThread(session);

        if (session.State != AccountSessionState.LoggedIn)
        {
            throw new InvalidOperationException(
                $"SignupFlow requires session '{session.Alias}' to be logged in; current state is {session.State}.");
        }

        if (session.UserId is not uint userId || userId == 0)
        {
            throw new InvalidOperationException(
                $"SignupFlow requires session '{session.Alias}' to have a non-zero UserId.");
        }

        var timeoutMs = ToTimeoutMilliseconds(signupTimeout);
        transport?.BindIncomingHandler(session, codec);

        try
        {
            await SendRequestAsync(
                session,
                transport,
                codec.CreateTourneySignupRequest(
                    userId,
                    match.TourneyId,
                    match.GameId,
                    matchPoint: match.ProductId,
                    nickname: session.Nickname));

            var signupMessage = await session.WaitForMessageAsync(
                ProtocolMessageKind.TourneySignupAck,
                timeoutMs);
            var signupAck = signupMessage.TourneySignupAcknowledgement
                ?? throw new InvalidOperationException("TourneySignupExAck missing in server response.");

            matchProgressState?.CaptureFromAnyMessage(signupMessage);

            var success = signupAck.Param == 0;
            var result = new SignupFlowResult
            {
                Success = success,
                SignupErrorCode = signupAck.Param,
                MobileAckParam = signupMessage.Param,
                Flags = signupAck.Flags,
                TourneyId = signupAck.Tourneyid,
                MatchPoint = signupAck.Matchpoint,
                GameId = signupAck.Gameid,
                FailureMessage = success ? null : $"Tourney signup failed with error code {signupAck.Param}.",
            };

            if (!success)
            {
                session.SetState(AccountSessionState.Failed);
                throw new InvalidOperationException(result.FailureMessage);
            }

            session.SetState(AccountSessionState.SignedUp);
            return result;
        }
        catch (TimeoutException)
        {
            session.SetState(AccountSessionState.Failed);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.SetState(AccountSessionState.Failed);
            throw;
        }
    }

    private static async CRpcTask SendRequestAsync(
        AccountSession session,
        IGameServerTransport? transport,
        Protocol.Generated.TKMobileReqMsg request)
    {
        await session.SendRequestAsync(request);
        if (transport is not null && session.LastSentPacket is not null)
        {
            await transport.SendAsync(session.LastSentPacket, session.Loop);
        }
    }

    private static int ToTimeoutMilliseconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
    }

    private static void EnsureOnLoopThread(AccountSession session)
    {
        if (!session.Loop.IsInLoopThread)
        {
            throw new InvalidOperationException("SignupFlow must run on the account session CRpcLoop thread.");
        }
    }
}
