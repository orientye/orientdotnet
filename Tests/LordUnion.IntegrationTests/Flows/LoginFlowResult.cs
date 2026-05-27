namespace LordUnion.IntegrationTests.Flows;

public sealed class LoginFlowResult
{
    public bool Success { get; init; }

    public uint? UserId { get; init; }

    public string? Nickname { get; init; }

    public string? AesKey { get; init; }

    public ulong? SessionId { get; init; }

    public uint LoginErrorCode { get; init; }

    public uint AnonymousRouteId { get; init; }

    public uint LoginRouteId { get; init; }

    public string? DecryptedLoginAckJson { get; init; }

    public string? FailureMessage { get; init; }
}
