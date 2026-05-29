using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Flows;

internal static class MatchStartMessageFactory
{
    public static void CaptureEmbeddedLobbyMatchStart(
        EnterMatchFlowSessionState state,
        ProtocolMessage sourceMessage)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sourceMessage);

        var lobby = sourceMessage.Acknowledgement?.LobbyAckMsg;
        if (lobby is null)
        {
            return;
        }

        if (lobby.StartclientexAckMsg is { } startClientEx)
        {
            state.CaptureMatchProgressMessage(CreateStartClientExMessage(startClientEx, sourceMessage.Header0));
        }

        if (lobby.StartgameclientAckMsg is { } startGameClient)
        {
            state.CaptureMatchProgressMessage(CreateStartGameClientMessage(startGameClient, sourceMessage.Header0));
        }
    }

    public static ProtocolMessage CreateStartClientExMessage(StartClientExAck ack, uint header0 = 1001)
    {
        ArgumentNullException.ThrowIfNull(ack);

        return new ProtocolMessage
        {
            Header0 = header0,
            Kind = ProtocolMessageKind.StartClientExAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    StartclientexAckMsg = ack,
                },
            },
        };
    }

    public static ProtocolMessage CreateStartGameClientMessage(StartGameClientAck ack, uint header0 = 1001)
    {
        ArgumentNullException.ThrowIfNull(ack);

        return new ProtocolMessage
        {
            Header0 = header0,
            Kind = ProtocolMessageKind.StartGameClientAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    StartgameclientAckMsg = ack,
                },
            },
        };
    }
}