using System.Reflection;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Protocol;

/// <summary>
/// Maps populated <see cref="MatchAckMsg"/> sub-messages to <see cref="ProtocolMessageKind"/>.
/// </summary>
internal static class MatchAckKindResolver
{
    private static readonly IReadOnlyDictionary<string, ProtocolMessageKind> KindByPropertyName =
        new Dictionary<string, ProtocolMessageKind>(StringComparer.Ordinal)
        {
            [nameof(MatchAckMsg.EnterroundAckMsg)] = ProtocolMessageKind.EnterRoundAck,
            [nameof(MatchAckMsg.EntermatchAckMsg)] = ProtocolMessageKind.EnterMatchAck,
            [nameof(MatchAckMsg.InitgametableAckMsg)] = ProtocolMessageKind.InitGameTableAck,
            [nameof(MatchAckMsg.TipmsgAckMsg)] = ProtocolMessageKind.MatchTipMsgAck,
            [nameof(MatchAckMsg.AddgameplayerinfoAckMsg)] = ProtocolMessageKind.AddGamePlayerInfoAck,
            [nameof(MatchAckMsg.ExitgameAckMsg)] = ProtocolMessageKind.ExitGameAck,
            [nameof(MatchAckMsg.OvergameAckMsg)] = ProtocolMessageKind.OverGameAck,
            [nameof(MatchAckMsg.HandoverAckMsg)] = ProtocolMessageKind.HandOverAck,
            [nameof(MatchAckMsg.PushmatchplayerinfoAckMsg)] = ProtocolMessageKind.PushMatchPlayerInfoAck,
        };

    public static ProtocolMessageKind Resolve(MatchAckMsg match)
    {
        ArgumentNullException.ThrowIfNull(match);

        foreach (var property in match.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.Name.EndsWith("AckMsg", StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsPopulated(property, match))
            {
                continue;
            }

            if (KindByPropertyName.TryGetValue(property.Name, out var kind))
            {
                return kind;
            }
        }

        return ProtocolMessageKind.Unknown;
    }

    public static bool HasMatchEndSignal(MatchAckMsg? match)
    {
        return match is not null
               && (IsPopulated(nameof(MatchAckMsg.OvergameAckMsg), match)
                   || IsPopulated(nameof(MatchAckMsg.HandoverAckMsg), match));
    }

    private static bool IsPopulated(string propertyName, MatchAckMsg match)
    {
        var property = match.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        return property is not null && IsPopulated(property, match);
    }

    private static bool IsPopulated(PropertyInfo property, object container)
    {
        var value = property.GetValue(container);
        if (value is null)
        {
            return false;
        }

        return property.PropertyType != typeof(string) || !string.IsNullOrEmpty((string)value);
    }
}