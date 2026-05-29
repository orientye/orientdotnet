using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Sessions;

public enum SessionMessageRouteArea
{
    Unknown,
    Lobby,
    Match,
    LordUnion,
}

public static class SessionMessageRouter
{
    public static SessionMessageRouteArea GetRouteArea(ProtocolMessage message)
    {
        return GetRouteArea(message.Kind);
    }

    public static SessionMessageRouteArea GetRouteArea(ProtocolMessageKind kind)
    {
        return kind switch
        {
            ProtocolMessageKind.AnonymousBrowseReq or ProtocolMessageKind.AnonymousBrowseAck
                or ProtocolMessageKind.CommonLoginReq or ProtocolMessageKind.CommonLoginAck
                or ProtocolMessageKind.TourneySignupReq or ProtocolMessageKind.TourneySignupAck
                or ProtocolMessageKind.TourneyUnsignupReq or ProtocolMessageKind.TourneyUnsignupAck
                or ProtocolMessageKind.StartGameClientAck or ProtocolMessageKind.StartClientExAck
                => SessionMessageRouteArea.Lobby,

            ProtocolMessageKind.EnterMatchReq or ProtocolMessageKind.EnterMatchAck
                or ProtocolMessageKind.EnterRoundReq or ProtocolMessageKind.EnterRoundAck
                or ProtocolMessageKind.InitGameTableAck or ProtocolMessageKind.MatchTipMsgAck
                or ProtocolMessageKind.AddGamePlayerInfoAck or ProtocolMessageKind.PushMatchPlayerInfoAck
                or ProtocolMessageKind.ExitGameReq or ProtocolMessageKind.ExitGameAck
                or ProtocolMessageKind.ExitMatchReq
                or ProtocolMessageKind.OverGameAck or ProtocolMessageKind.HandOverAck
                => SessionMessageRouteArea.Match,

            ProtocolMessageKind.LordReq or ProtocolMessageKind.LordAck
                => SessionMessageRouteArea.LordUnion,

            _ => SessionMessageRouteArea.Unknown,
        };
    }

    public static string DescribeMessage(ProtocolMessage message)
    {
        var route = GetRouteArea(message);
        var keyIds = FormatKeyIds(message);
        var ackDetail = FormatUnidentifiedAckDetail(message);
        return $"{message.Kind} route={route} header0={message.Header0}{ackDetail}{keyIds}";
    }

    private static string FormatUnidentifiedAckDetail(ProtocolMessage message)
    {
        if (message.Kind != ProtocolMessageKind.Unknown || message.Acknowledgement is not { } ack)
        {
            return string.Empty;
        }

        return $", ack={AckBodyDescriber.DescribeUnidentified(ack)}";
    }

    private static string FormatKeyIds(ProtocolMessage message)
    {
        var parts = new List<string>();

        if (message.Param != 0)
        {
            parts.Add($"param={message.Param}");
        }

        if (message.CommonLoginAcknowledgement?.Userinfo?.Userid is uint loginUserId and > 0)
        {
            parts.Add($"userid={loginUserId}");
        }

        if (message.TourneySignupRequest?.Userid is uint signupUserId and > 0)
        {
            parts.Add($"userid={signupUserId}");
        }

        if (message.TourneySignupAcknowledgement?.Tourneyid is uint tourneyId and > 0)
        {
            parts.Add($"tourneyid={tourneyId}");
        }

        if (message.TourneySignupAcknowledgement is { } signupAck)
        {
            parts.Add($"signup.param={signupAck.Param}");
            if (signupAck.Flags != 0)
            {
                parts.Add($"signup.flags={signupAck.Flags}");
            }
        }

        if (message.StartGameClientAcknowledgement?.Matchid is uint startMatchId and > 0)
        {
            parts.Add($"matchid={startMatchId}");
        }

        if (message.StartClientExAcknowledgement?.Matchid is uint startExMatchId and > 0)
        {
            parts.Add($"matchid={startExMatchId}");
        }

        if (message.EnterMatchAcknowledgement?.Matchid is uint enterMatchAckId and > 0)
        {
            parts.Add($"matchid={enterMatchAckId}");
        }

        if (message.EnterRoundAcknowledgement?.Seatorder is uint seatOrder)
        {
            parts.Add($"seatorder={seatOrder}");
        }

        if (message.Acknowledgement?.MatchAckMsg?.AddgameplayerinfoAckMsg?.Playerinfo is { } addPlayerInfo)
        {
            parts.Add($"player.seatorder={addPlayerInfo.Seatorder}");
            if (addPlayerInfo.Userid > 0)
            {
                parts.Add($"player.userid={addPlayerInfo.Userid}");
            }

            if (addPlayerInfo.Userid64 > 0)
            {
                parts.Add($"player.userid64={addPlayerInfo.Userid64}");
            }
        }

        if (message.Request?.MatchReqMsg?.Matchid is uint enterMatchId and > 0)
        {
            parts.Add($"matchid={enterMatchId}");
        }

        if (message.EnterMatchRequest?.Gameid is uint enterMatchGameId and > 0)
        {
            parts.Add($"gameid={enterMatchGameId}");
        }

        if (message.EnterRoundRequest?.Gameid is uint enterRoundGameId and > 0)
        {
            parts.Add($"gameid={enterRoundGameId}");
        }

        if (message.LordRequest?.Matchid is uint lordMatchId and > 0)
        {
            parts.Add($"matchid={lordMatchId}");
        }

        if (message.LordAcknowledgement?.Matchid is uint lordAckMatchId and > 0)
        {
            parts.Add($"matchid={lordAckMatchId}");
        }

        if (message.LordAcknowledgement is { } lordAck)
        {
            parts.Add($"lord={LordAckDescriber.Describe(lordAck)}");
        }

        return parts.Count == 0 ? string.Empty : ", " + string.Join(", ", parts);
    }
}