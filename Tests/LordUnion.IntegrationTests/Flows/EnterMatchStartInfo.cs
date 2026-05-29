namespace LordUnion.IntegrationTests.Flows;

using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;

public sealed class EnterMatchStartInfo
{
    public required uint MatchId { get; init; }

    public required uint GameId { get; init; }

    public required uint TourneyId { get; init; }

    public required uint MatchPoint { get; init; }

    public required byte[] Ticket { get; init; }
}

public sealed class EnterMatchFlowSessionState
{
    public Protocol.Generated.InitGameTableAck? InitGameTableAck { get; set; }

    public Protocol.Generated.EnterRoundAck? LastEnterRoundAck { get; set; }

    public ProtocolMessage? CapturedMatchStartMessage { get; private set; }

    private readonly Dictionary<uint, uint> seatByUserId = new();

    public void CaptureFromAnyMessage(ProtocolMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        CaptureMatchProgressMessage(message);
        MatchStartMessageFactory.CaptureEmbeddedLobbyMatchStart(this, message);
    }

    public void CaptureMatchProgressMessage(ProtocolMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Kind is ProtocolMessageKind.StartGameClientAck or ProtocolMessageKind.StartClientExAck)
        {
            CapturedMatchStartMessage = message;
            return;
        }

        if (message.Kind == ProtocolMessageKind.InitGameTableAck)
        {
            InitGameTableAck = message.InitGameTableAcknowledgement;
            return;
        }

        if (message.Kind != ProtocolMessageKind.AddGamePlayerInfoAck)
        {
            if (message.Kind == ProtocolMessageKind.EnterRoundAck)
            {
                LastEnterRoundAck = message.EnterRoundAcknowledgement;
            }

            return;
        }

        var player = message.Acknowledgement?.MatchAckMsg?.AddgameplayerinfoAckMsg?.Playerinfo;
        if (player is null)
        {
            return;
        }

        if (!GamePlayerInfoUserId.ResolveAll(player).Any())
        {
            return;
        }

        foreach (var playerUserId in GamePlayerInfoUserId.ResolveAll(player))
        {
            seatByUserId[playerUserId] = player.Seatorder;
        }
    }

    public bool TryGetSeatForUser(uint userId, out uint seatOrder)
    {
        if (seatByUserId.TryGetValue(userId, out seatOrder))
        {
            return true;
        }

        seatOrder = 0;
        return false;
    }

    public uint? GetSeatForUser(uint userId)
    {
        return TryGetSeatForUser(userId, out var seat) ? seat : null;
    }
}

internal static class GamePlayerInfoUserId
{
    public static IEnumerable<uint> ResolveAll(GamePlayerInfo player)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.Userid64 > 0)
        {
            yield return (uint)player.Userid64;
        }

        if (player.Userid > 0)
        {
            yield return player.Userid;
        }
    }

    public static bool MatchesSessionUser(GamePlayerInfo player, uint sessionUserId)
    {
        return ResolveAll(player).Any(resolvedUserId => resolvedUserId == sessionUserId);
    }
}