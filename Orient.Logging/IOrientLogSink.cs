namespace Orient.Logging;

public interface IOrientLogSink
{
    void Write(IReadOnlyList<OrientLogEvent> batch);
    void Flush();
}
