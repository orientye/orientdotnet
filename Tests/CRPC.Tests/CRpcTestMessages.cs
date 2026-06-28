using Orient.Rpc.Codec;

namespace CRPC.Tests;

internal static class CRpcTestMessages
{
    public static CRpcMessage CreateRequest(
        ushort serviceId,
        ushort methodId = 1,
        long reqSequence = 1,
        byte[]? body = null)
    {
        return CRpcMessage.Create(
            CRpcMessageType.Request,
            serviceId,
            methodId,
            reqSequence,
            resultCode: 0,
            body: body ?? Array.Empty<byte>());
    }

    public static CRpcMessage CreateResponse(
        ushort serviceId,
        ushort methodId,
        long reqSequence,
        int resultCode = 0,
        byte[]? body = null)
    {
        return CRpcMessage.Create(
            CRpcMessageType.Response,
            serviceId,
            methodId,
            reqSequence,
            resultCode,
            body: body ?? Array.Empty<byte>());
    }

    public static CRpcMessage CreatePush(
        ushort serviceId,
        ushort methodId,
        byte[]? body = null)
    {
        return CRpcMessage.Create(
            CRpcMessageType.Push,
            serviceId,
            methodId,
            reqSequence: 0,
            resultCode: 0,
            body: body ?? Array.Empty<byte>());
    }
}
