namespace Orient.Rpc.Codec;

public enum CRpcMessageType : byte
{
    Request = 0,
    Response = 1,
    Push = 2,
    Heartbeat = 3,
    // HeartbeatAck = 4 reserved for Phase 2; not implemented in v1
}
