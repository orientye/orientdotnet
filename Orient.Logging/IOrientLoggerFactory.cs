namespace Orient.Logging;

public interface IOrientLoggerFactory
{
    IOrientLogger CreateLogger(string category);
}
