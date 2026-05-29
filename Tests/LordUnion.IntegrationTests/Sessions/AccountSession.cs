using CRpc.Async;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Sessions;

public sealed class AccountSession
{
    private readonly ServerProtocolCodec codec;
    private readonly List<SessionMessageLogEntry> sentMessages = new();
    private readonly List<SessionMessageLogEntry> receivedMessages = new();
    private readonly List<byte[]> sentPackets = new();
    private PendingMessageWait? pendingWait;

    public AccountSession(CRpcLoop loop, string alias, ServerProtocolCodec codec)
    {
        Loop = loop ?? throw new ArgumentNullException(nameof(loop));
        Alias = alias ?? throw new ArgumentNullException(nameof(alias));
        this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        State = AccountSessionState.Disconnected;
        CurrentPhase = MapStateToPhase(State);
    }

    public CRpcLoop Loop { get; }

    public string Alias { get; }

    public AccountSessionState State { get; private set; }

    public ProtocolPhase CurrentPhase { get; private set; }

    public uint? UserId { get; set; }

    public string? Nickname { get; set; }

    public string? AesKey { get; set; }

    public ulong? SessionId { get; set; }

    public uint? LoginErrorCode { get; set; }

    public uint? AnonymousRouteId { get; set; }

    public uint? LoginRouteId { get; set; }

    public uint? MatchId { get; set; }

    public byte[]? Ticket { get; set; }

    public uint? TableId { get; set; }

    public uint? SeatOrder { get; set; }

    public uint? TourneyId { get; set; }

    public uint? MatchPoint { get; set; }

    public byte[]? LastSentPacket { get; private set; }

    public IReadOnlyList<byte[]> SentPackets => sentPackets;

    public IReadOnlyList<SessionMessageLogEntry> SentMessages => sentMessages;

    public IReadOnlyList<SessionMessageLogEntry> ReceivedMessages => receivedMessages;

    public IEnumerable<SessionMessageLogEntry> MessageLog =>
        sentMessages.Concat(receivedMessages).OrderBy(entry => entry.Timestamp);

    public Action<ProtocolMessage>? PushMessageReceived { get; set; }

    public void SetState(AccountSessionState state)
    {
        EnsureInLoopThread();
        State = state;
        CurrentPhase = MapStateToPhase(state);
    }

    public CRpcTask SendRequestAsync(TKMobileReqMsg request)
    {
        EnsureInLoopThread();
        ArgumentNullException.ThrowIfNull(request);

        var packet = codec.EncodeClientRequest(request);
        LastSentPacket = packet;
        sentPackets.Add(packet);

        var kind = IdentifyRequestKind(request);
        sentMessages.Add(CreateLogEntry(
            SessionMessageDirection.Sent,
            kind,
            header0: ServerPacketFrame.ClientSendHeaderMagic,
            param: request.Param,
            userId: ExtractRequestUserId(request),
            description: SessionMessageRouter.DescribeMessage(new ProtocolMessage
            {
                Header0 = ServerPacketFrame.ClientSendHeaderMagic,
                Kind = kind,
                Param = request.Param,
                Request = request,
            })));

        return CRpcTask.CompletedTask(Loop);
    }

    public void DeliverIncomingMessage(ProtocolMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Loop.Post(() => HandleIncomingMessage(message));
    }

    public CRpcTask<ProtocolMessage> WaitForMessageAsync(
        ProtocolMessageKind kind,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        return WaitForMessageAsync([kind], timeoutMs, cancellationToken);
    }

    public CRpcTask<ProtocolMessage> WaitForMessageAsync(
        IReadOnlyList<ProtocolMessageKind> kinds,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        EnsureInLoopThread();
        if (kinds.Count == 0)
        {
            throw new ArgumentException("At least one message kind is required.", nameof(kinds));
        }

        var kindLabel = kinds.Count == 1
            ? kinds[0].ToString()
            : string.Join(" or ", kinds);
        return WaitForMessageAsync(
            message => kinds.Contains(message.Kind),
            kindLabel,
            timeoutMs,
            cancellationToken);
    }

    public CRpcTask<ProtocolMessage> WaitForMessageAsync(
        Func<ProtocolMessage, bool> matcher,
        string matcherLabel,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        EnsureInLoopThread();
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(matcherLabel);

        if (pendingWait is not null)
        {
            throw new InvalidOperationException(
                $"Account '{Alias}' already has an active wait for {pendingWait.KindLabel} in state {State}.");
        }

        return WaitForMessageCoreAsync(matcher, matcherLabel, timeoutMs, cancellationToken);
    }

    private async CRpcTask<ProtocolMessage> WaitForMessageCoreAsync(
        Func<ProtocolMessage, bool> matcher,
        string matcherLabel,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var source = new CRpcTaskCompletionSource<ProtocolMessage>(Loop);
        pendingWait = new PendingMessageWait(matcher, matcherLabel, source);

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                if (Loop.IsInLoopThread)
                {
                    CancelPendingWait(cancellationToken);
                }
                else
                {
                    Loop.Post(() => CancelPendingWait(cancellationToken));
                }
            });
        }

        if (timeoutMs >= 0)
        {
            _ = ArmWaitTimeoutAsync(matcherLabel, timeoutMs, source);
        }

        return await source.Task;
    }

    private async CRpcTask ArmWaitTimeoutAsync(
        string matcherLabel,
        int timeoutMs,
        CRpcTaskCompletionSource<ProtocolMessage> source)
    {
        await CRpcTask.Delay(timeoutMs, Loop);

        if (pendingWait?.Source != source)
        {
            return;
        }

        pendingWait = null;
        source.TrySetException(CreateWaitTimeoutException(matcherLabel));
    }

    private void HandleIncomingMessage(ProtocolMessage message)
    {
        receivedMessages.Add(CreateLogEntry(
            SessionMessageDirection.Received,
            message.Kind,
            message.Header0,
            message.Param == 0 ? null : message.Param,
            ExtractAckUserId(message),
            SessionMessageRouter.DescribeMessage(message)));

        if (pendingWait is not null && pendingWait.Matches(message))
        {
            var wait = pendingWait;
            pendingWait = null;
            wait.Source.TrySetResult(message);
            return;
        }

        PushMessageReceived?.Invoke(message);
    }

    private void CancelPendingWait(CancellationToken cancellationToken)
    {
        if (pendingWait is null)
        {
            return;
        }

        var wait = pendingWait;
        pendingWait = null;
        wait.Source.TrySetCanceled();
    }

    private void EnsureInLoopThread()
    {
        if (!Loop.IsInLoopThread)
        {
            throw new InvalidOperationException("AccountSession operations must run on the owner CRpcLoop thread.");
        }
    }

    private SessionMessageLogEntry CreateLogEntry(
        SessionMessageDirection direction,
        ProtocolMessageKind kind,
        uint header0,
        uint? param,
        uint? userId,
        string description)
    {
        return new SessionMessageLogEntry
        {
            Direction = direction,
            AccountAlias = Alias,
            State = State,
            Phase = CurrentPhase,
            Kind = kind,
            Header0 = header0,
            Param = param,
            UserId = userId ?? UserId,
            Timestamp = DateTimeOffset.UtcNow,
            Description = description,
        };
    }

    private TimeoutException CreateWaitTimeoutException(string matcherLabel)
    {
        return new TimeoutException(
            $"Timed out waiting for {matcherLabel} on account '{Alias}' in state {State} (phase {CurrentPhase}).");
    }

    private static uint? ExtractRequestUserId(TKMobileReqMsg request)
    {
        if (request.LobbyReqMsg?.TourneysignupReqMsg?.Userid is uint signupUserId and > 0)
        {
            return signupUserId;
        }

        return null;
    }

    private static uint? ExtractAckUserId(ProtocolMessage message)
    {
        if (message.CommonLoginAcknowledgement?.Userinfo?.Userid is uint loginUserId and > 0)
        {
            return loginUserId;
        }

        return null;
    }

    private static ProtocolMessageKind IdentifyRequestKind(TKMobileReqMsg request)
    {
        if (request.LobbyReqMsg?.AnonymousReqMsg != null)
        {
            return ProtocolMessageKind.AnonymousBrowseReq;
        }

        if (request.LobbyReqMsg?.CommonloginReqMsg != null)
        {
            return ProtocolMessageKind.CommonLoginReq;
        }

        if (request.LobbyReqMsg?.TourneysignupReqMsg != null)
        {
            return ProtocolMessageKind.TourneySignupReq;
        }

        if (request.LobbyReqMsg?.TourneyunsignupReqMsg != null)
        {
            return ProtocolMessageKind.TourneyUnsignupReq;
        }

        if (request.MatchReqMsg?.EntermatchReqMsg != null)
        {
            return ProtocolMessageKind.EnterMatchReq;
        }

        if (request.MatchReqMsg?.EnterroundReqMsg != null)
        {
            return ProtocolMessageKind.EnterRoundReq;
        }

        if (request.MatchReqMsg?.ExitgameReqMsg != null)
        {
            return ProtocolMessageKind.ExitGameReq;
        }

        if (request.MatchReqMsg?.ExitmatchReqMsg != null)
        {
            return ProtocolMessageKind.ExitMatchReq;
        }

        if (request.LordReqMsg != null)
        {
            return ProtocolMessageKind.LordReq;
        }

        return ProtocolMessageKind.Unknown;
    }

    private static ProtocolPhase MapStateToPhase(AccountSessionState state)
    {
        return state switch
        {
            AccountSessionState.Disconnected or AccountSessionState.Connecting => ProtocolPhase.Connect,
            AccountSessionState.Connected => ProtocolPhase.AnonymousBrowse,
            AccountSessionState.LoggedIn => ProtocolPhase.Login,
            AccountSessionState.SignedUp or AccountSessionState.WaitingForMatch => ProtocolPhase.Signup,
            AccountSessionState.EnteringMatch => ProtocolPhase.EnterMatch,
            AccountSessionState.InGame or AccountSessionState.Finished => ProtocolPhase.Game,
            AccountSessionState.Failed => ProtocolPhase.Unknown,
            _ => ProtocolPhase.Unknown,
        };
    }

    private sealed class PendingMessageWait
    {
        public PendingMessageWait(
            Func<ProtocolMessage, bool> matcher,
            string kindLabel,
            CRpcTaskCompletionSource<ProtocolMessage> source)
        {
            Matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            KindLabel = kindLabel ?? throw new ArgumentNullException(nameof(kindLabel));
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public Func<ProtocolMessage, bool> Matcher { get; }

        public string KindLabel { get; }

        public CRpcTaskCompletionSource<ProtocolMessage> Source { get; }

        public bool Matches(ProtocolMessage message) => Matcher(message);
    }
}