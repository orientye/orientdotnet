using CRpcOptions;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace CRpcProtobufPlugin;

internal static class CRpcOptionsReader
{
    private const int ServiceIdFieldNumber = 60001;
    private const int MethodIdFieldNumber = 60002;
    private const int ServerPushFieldNumber = 60003;

    public static bool TryGetServiceId(ServiceOptions options, out int serviceId)
    {
        if (options.HasExtension(CrpcOptionsExtensions.ServiceId))
        {
            serviceId = options.GetExtension(CrpcOptionsExtensions.ServiceId);
            return true;
        }

        return TryGetInt32(options, ServiceIdFieldNumber, out serviceId);
    }

    public static bool TryGetMethodId(MethodOptions options, out int methodId)
    {
        if (options.HasExtension(CrpcOptionsExtensions.MethodId))
        {
            methodId = options.GetExtension(CrpcOptionsExtensions.MethodId);
            return true;
        }

        return TryGetInt32(options, MethodIdFieldNumber, out methodId);
    }

    public static bool TryGetServerPush(MethodOptions options, out bool serverPush)
    {
        if (options.HasExtension(CrpcOptionsExtensions.ServerPush))
        {
            serverPush = options.GetExtension(CrpcOptionsExtensions.ServerPush);
            return true;
        }

        return TryGetBool(options, ServerPushFieldNumber, out serverPush);
    }

    private static bool TryGetInt32(IMessage message, int fieldNumber, out int value)
    {
        var input = new CodedInputStream(message.ToByteArray());
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (WireFormat.GetTagFieldNumber(tag) == fieldNumber
                && WireFormat.GetTagWireType(tag) == WireFormat.WireType.Varint)
            {
                value = input.ReadInt32();
                return true;
            }

            input.SkipLastField();
        }

        value = 0;
        return false;
    }

    private static bool TryGetBool(IMessage message, int fieldNumber, out bool value)
    {
        var input = new CodedInputStream(message.ToByteArray());
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (WireFormat.GetTagFieldNumber(tag) == fieldNumber
                && WireFormat.GetTagWireType(tag) == WireFormat.WireType.Varint)
            {
                value = input.ReadBool();
                return true;
            }

            input.SkipLastField();
        }

        value = false;
        return false;
    }
}