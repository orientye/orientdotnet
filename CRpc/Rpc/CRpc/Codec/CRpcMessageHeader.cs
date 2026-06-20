using DotNetty.Buffers;

namespace CRpc.Rpc.CRpc.Codec;

public sealed class CRpcMessageHeader
{
    public const int FixedLength = 24;
    public const byte ProtocolVersion = 1;

    public byte Version { get; init; } = ProtocolVersion;
    public CRpcMessageType MessageType { get; init; }
    public byte Flags { get; init; }
    public byte Reserved { get; init; }
    public ushort ServiceId { get; init; }
    public ushort MethodId { get; init; }
    public long ReqSequence { get; init; }
    public int ResultCode { get; init; }
    public int BodyOriginLen { get; init; }

    public static CRpcMessageHeader Create(
        CRpcMessageType messageType,
        ushort serviceId,
        ushort methodId,
        long reqSequence,
        int resultCode,
        byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new CRpcMessageHeader
        {
            MessageType = messageType,
            Flags = CRpcFrameFlags.None,
            ServiceId = serviceId,
            MethodId = methodId,
            ReqSequence = reqSequence,
            ResultCode = resultCode,
            BodyOriginLen = body.Length,
        };
    }

    public static CRpcMessageHeader ReadFrom(IByteBuffer buffer)
    {
        if (buffer.ReadableBytes < FixedLength)
        {
            throw new InvalidDataException(
                $"Buffer readable bytes {buffer.ReadableBytes} is less than header length {FixedLength}.");
        }

        var version = buffer.ReadByte();
        if (version != ProtocolVersion)
        {
            throw new InvalidDataException($"Unsupported CRpc protocol version {version}.");
        }

        var messageType = (CRpcMessageType)buffer.ReadByte();
        if (messageType > CRpcMessageType.Push)
        {
            throw new InvalidDataException($"Unsupported CRpc message type {(byte)messageType}.");
        }

        var flags = buffer.ReadByte();
        if (flags != CRpcFrameFlags.None)
        {
            throw new InvalidDataException($"Unsupported CRpc frame flags 0x{flags:X2}.");
        }

        _ = buffer.ReadByte(); // reserved

        return new CRpcMessageHeader
        {
            Version = version,
            MessageType = messageType,
            Flags = flags,
            ServiceId = buffer.ReadUnsignedShort(),
            MethodId = buffer.ReadUnsignedShort(),
            ReqSequence = buffer.ReadLong(),
            ResultCode = buffer.ReadInt(),
            BodyOriginLen = buffer.ReadInt(),
        };
    }

    public void WriteTo(IByteBuffer buffer)
    {
        buffer.WriteByte(Version);
        buffer.WriteByte((byte)MessageType);
        buffer.WriteByte(Flags);
        buffer.WriteByte(Reserved);
        buffer.WriteShort(unchecked((short)ServiceId));
        buffer.WriteShort(unchecked((short)MethodId));
        buffer.WriteLong(ReqSequence);
        buffer.WriteInt(ResultCode);
        buffer.WriteInt(BodyOriginLen);
    }

    public CRpcMessageHeader CreateResponse(int resultCode, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new CRpcMessageHeader
        {
            MessageType = CRpcMessageType.Response,
            Flags = CRpcFrameFlags.None,
            ServiceId = ServiceId,
            MethodId = MethodId,
            ReqSequence = ReqSequence,
            ResultCode = resultCode,
            BodyOriginLen = body.Length,
        };
    }
}
