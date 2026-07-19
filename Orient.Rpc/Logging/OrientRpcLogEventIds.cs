namespace Orient.Rpc.Logging;

internal static class OrientRpcLogEventIds
{
    public const int IgnoredMessageType = 2001;
    public const int UnhandledPush = 2002;
    public const int PushHandlerException = 2003;
    public const int CallTimeout = 2004;
    public const int ProcessException = 2005;
    public const int ClientDisconnected = 2006;
    public const int RemoteDisconnect = 2007;
    public const int ChannelException = 2008;
    public const int DecodeFailed = 2009;
    public const int WriteBufferWarning = 2010;
    public const int ServerStarted = 2011;
}
