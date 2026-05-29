namespace LordUnion.IntegrationTests.Flows;

public sealed class SignupFlowResult
{
    public bool Success { get; init; }

    /// <summary>Inner <c>TourneySignupExAck.param</c> — 0 means signup accepted.</summary>
    public uint SignupErrorCode { get; init; }

    /// <summary>Outer <c>TKMobileAckMsg.param</c> on the signup ack frame.</summary>
    public uint MobileAckParam { get; init; }

    public int Flags { get; init; }

    public uint TourneyId { get; init; }

    public uint MatchPoint { get; init; }

    public int GameId { get; init; }

    public string? FailureMessage { get; init; }
}