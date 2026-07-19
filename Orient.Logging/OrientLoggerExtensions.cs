namespace Orient.Logging;

public static class OrientLoggerExtensions
{
    public static void Info(this IOrientLogger logger, int eventId, string message)
        => logger.Log(OrientLogLevel.Info, eventId, message);

    public static void Warn(this IOrientLogger logger, int eventId, string message, Exception? ex = null)
        => logger.Log(OrientLogLevel.Warn, eventId, message, ex);

    public static void Error(this IOrientLogger logger, int eventId, string eventMessage, Exception? ex = null)
        => logger.Log(OrientLogLevel.Error, eventId, eventMessage, ex);
}
