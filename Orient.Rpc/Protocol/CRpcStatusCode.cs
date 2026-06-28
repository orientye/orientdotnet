namespace Orient.Rpc.Protocol;

/// <summary>
/// Framework-level result codes carried in the CRpc response header <c>resultCode</c> field.
/// Application codes use <see cref="ApplicationRangeStart"/> and above.
/// </summary>
public enum CRpcStatusCode
{
    Ok = 0,

    ServiceNotFound = 1001,
    MethodNotFound = 1002,
    InvalidRequest = 1003,
    InternalError = 1004,
    Unavailable = 1005,
    DeadlineExceeded = 1006,
}

public static class CRpcStatusCodeExtensions
{
    public const int ApplicationRangeStart = 10001;

    public const int FrameworkRangeStart = 1000;
    public const int FrameworkRangeEnd = 1999;

    public static bool IsFrameworkCode(int code)
    {
        return code >= FrameworkRangeStart && code <= FrameworkRangeEnd;
    }

    public static bool IsApplicationCode(int code)
    {
        return code >= ApplicationRangeStart;
    }
}
