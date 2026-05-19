using Google.Protobuf;

namespace CRpc.Rpc;

public interface IRpcHttpJsonCodec
{
    bool TryGetHttpMethodParsers(
        ushort methodId,
        out MessageParser requestParser,
        out MessageParser responseParser);
}
