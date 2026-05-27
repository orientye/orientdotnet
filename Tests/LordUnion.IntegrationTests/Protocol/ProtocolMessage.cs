using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Protocol;

public enum ProtocolPhase
{
    Unknown,
    Connect,
    AnonymousBrowse,
    Login,
    Signup,
    EnterMatch,
    EnterRound,
    Game,
}

public enum ProtocolMessageKind
{
    Unknown,
    EmptyBody,
    AnonymousBrowseReq,
    AnonymousBrowseAck,
    CommonLoginReq,
    CommonLoginAck,
    TourneySignupReq,
    TourneySignupAck,
    TourneyUnsignupReq,
    TourneyUnsignupAck,
    ExitGameReq,
    ExitGameAck,
    ExitMatchReq,
    StartGameClientAck,
    StartClientExAck,
    EnterMatchReq,
    EnterMatchAck,
    EnterRoundReq,
    EnterRoundAck,
    InitGameTableAck,
    MatchTipMsgAck,
    AddGamePlayerInfoAck,
    PushMatchPlayerInfoAck,
    OverGameAck,
    HandOverAck,
    LordReq,
    LordAck,
}

public sealed class ProtocolDecodeContext
{
    public string? AccountAlias { get; init; }

    public ProtocolPhase Phase { get; init; } = ProtocolPhase.Unknown;

    public uint? ExpectedHeader0 { get; init; }
}

public sealed class ProtocolDecodeError
{
    public string? AccountAlias { get; init; }

    public ProtocolPhase Phase { get; init; }

    public uint? Header0 { get; init; }

    public int? BodyLength { get; init; }

    public uint? MessageId { get; init; }

    public string Message { get; init; } = string.Empty;

    public override string ToString()
    {
        return
            $"alias={AccountAlias ?? "?"}, phase={Phase}, header0={Header0}, bodyLength={BodyLength}, messageId={MessageId}, message={Message}";
    }
}

public sealed class ProtocolMessage
{
    public uint Header0 { get; init; }

    public int BodyLength { get; init; }

    public ProtocolMessageKind Kind { get; init; }

    public uint Param { get; init; }

    public TKMobileReqMsg? Request { get; init; }

    public TKMobileAckMsg? Acknowledgement { get; init; }

    public bool IsRequest => Request != null;

    public bool IsAcknowledgement => Acknowledgement != null;

    public AnonymousBrowseReq? AnonymousBrowseRequest =>
        Request?.LobbyReqMsg?.AnonymousReqMsg;

    public AnonymousBrowseAck? AnonymousBrowseAcknowledgement =>
        Acknowledgement?.LobbyAckMsg?.AnonymousAckMsg;

    public CommonLoginReq? CommonLoginRequest =>
        Request?.LobbyReqMsg?.CommonloginReqMsg;

    public CommonLoginAck? CommonLoginAcknowledgement =>
        Acknowledgement?.LobbyAckMsg?.CommonloginAckMsg;

    public TourneySignupReq? TourneySignupRequest =>
        Request?.LobbyReqMsg?.TourneysignupReqMsg;

    public TourneySignupExAck? TourneySignupAcknowledgement =>
        Acknowledgement?.LobbyAckMsg?.TourneysignupexAckMsg;

    public TourneyUnsignupReq? TourneyUnsignupRequest =>
        Request?.LobbyReqMsg?.TourneyunsignupReqMsg;

    public TourneyUnsignupAck? TourneyUnsignupAcknowledgement =>
        Acknowledgement?.LobbyAckMsg?.TourneyunsignupAckMsg;

    public ExitGameReq? ExitGameRequest =>
        Request?.MatchReqMsg?.ExitgameReqMsg;

    public ExitGameAck? ExitGameAcknowledgement =>
        Acknowledgement?.MatchAckMsg?.ExitgameAckMsg;

    public ExitMatchReq? ExitMatchRequest =>
        Request?.MatchReqMsg?.ExitmatchReqMsg;

    public StartGameClientAck? StartGameClientAcknowledgement =>
        Acknowledgement?.LobbyAckMsg?.StartgameclientAckMsg;

    public StartClientExAck? StartClientExAcknowledgement =>
        Acknowledgement?.LobbyAckMsg?.StartclientexAckMsg;

    public EnterMatchReq? EnterMatchRequest =>
        Request?.MatchReqMsg?.EntermatchReqMsg;

    public EnterMatchAck? EnterMatchAcknowledgement =>
        Acknowledgement?.MatchAckMsg?.EntermatchAckMsg;

    public EnterRoundReq? EnterRoundRequest =>
        Request?.MatchReqMsg?.EnterroundReqMsg;

    public EnterRoundAck? EnterRoundAcknowledgement =>
        Acknowledgement?.MatchAckMsg?.EnterroundAckMsg;

    public InitGameTableAck? InitGameTableAcknowledgement =>
        Acknowledgement?.MatchAckMsg?.InitgametableAckMsg;

    public OverGameAck? OverGameAcknowledgement =>
        Acknowledgement?.MatchAckMsg?.OvergameAckMsg;

    public HandOverAck? HandOverAcknowledgement =>
        Acknowledgement?.MatchAckMsg?.HandoverAckMsg;

    public LordReqMsg? LordRequest =>
        Request?.LordReqMsg;

    public LordAckMsg? LordAcknowledgement =>
        Acknowledgement?.LordAckMsg;

    public bool HasGameFinishedSignal =>
        LordAcknowledgement?.LordresultAckMsg is not null
        || OverGameAcknowledgement is not null
        || HandOverAcknowledgement is not null
        || MatchAckKindResolver.HasMatchEndSignal(Acknowledgement?.MatchAckMsg);
}

public static class AnonymousBrowseAckExtensions
{
    public static long ResolveLoginTimestampMillis(this AnonymousBrowseAck ack, long elapsedMillis = 0)
    {
        if (ack.U64servertime > 0)
        {
            return (long)ack.U64servertime + elapsedMillis;
        }

        if (ack.Servertime > 0)
        {
            return ((long)ack.Servertime * 1000) + elapsedMillis;
        }

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
