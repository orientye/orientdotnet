namespace Orient.Rpc.Codec;

/// <summary>
/// CRpc frame header flags (offset 2). Protocol v1 requires zero; non-zero values are rejected.
/// Future versions may define bit flags here (e.g. compression); negotiate via a higher protocol version.
/// </summary>
public static class CRpcFrameFlags
{
    public const byte None = 0;
}
