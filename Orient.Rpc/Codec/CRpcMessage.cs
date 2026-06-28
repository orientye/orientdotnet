using DotNetty.Buffers;

namespace Orient.Rpc.Codec;

public sealed class CRpcMessage : IRpcMessage
{
    public const int Magic = 0x43525043; // 'CRPC'
    public const int FramePrefixLength = 8;
    public const int MinFrameLength = FramePrefixLength + CRpcMessageHeader.FixedLength;

    private static readonly byte[] EmptyBody = Array.Empty<byte>();

    public CRpcMessageHeader Header { get; }
    public byte[] Body { get; private set; }

    private CRpcMessage(CRpcMessageHeader header, byte[] body)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Body = body ?? EmptyBody;
    }

    public ushort ServiceId => Header.ServiceId;
    public ushort MethodId => Header.MethodId;
    public long ReqSequence => Header.ReqSequence;
    public int ResultCode => Header.ResultCode;
    public CRpcMessageType MessageType => Header.MessageType;

    public static CRpcMessage Create(
        CRpcMessageType messageType,
        ushort serviceId,
        ushort methodId,
        long reqSequence,
        int resultCode,
        byte[] body)
    {
        var header = CRpcMessageHeader.Create(
            messageType,
            serviceId,
            methodId,
            reqSequence,
            resultCode,
            body ?? EmptyBody);
        return new CRpcMessage(header, body ?? EmptyBody);
    }

    public static CRpcMessage CreateHeartbeat()
    {
        return Create(
            CRpcMessageType.Heartbeat,
            serviceId: 0,
            methodId: 0,
            reqSequence: 0,
            resultCode: 0,
            EmptyBody);
    }

    public static CRpcMessage ReadFrom(IByteBuffer frame)
    {
        if (frame.ReadableBytes < FramePrefixLength)
        {
            throw new InvalidDataException("CRpc frame prefix is incomplete.");
        }

        var magic = frame.ReadInt();
        if (magic != Magic)
        {
            throw new InvalidDataException($"Invalid CRpc magic 0x{magic:X8}.");
        }

        var payloadLength = frame.ReadInt();
        if (payloadLength < CRpcMessageHeader.FixedLength)
        {
            throw new InvalidDataException($"Invalid CRpc payload length {payloadLength}.");
        }

        return ReadFromPayload(frame, payloadLength);
    }

    /// <summary>
    /// Reads header + body from a buffer whose first readable byte is the fixed header.
    /// Used after the frame prefix (magic + payloadLen) has been stripped by the decoder.
    /// </summary>
    public static CRpcMessage ReadFromPayload(IByteBuffer payload, int payloadLength)
    {
        if (payloadLength < CRpcMessageHeader.FixedLength)
        {
            throw new InvalidDataException($"Invalid CRpc payload length {payloadLength}.");
        }

        if (payload.ReadableBytes < payloadLength)
        {
            throw new InvalidDataException("CRpc frame payload is incomplete.");
        }

        var header = CRpcMessageHeader.ReadFrom(payload);
        var bodyLength = payloadLength - CRpcMessageHeader.FixedLength;
        if (payload.ReadableBytes < bodyLength)
        {
            throw new InvalidDataException("CRpc frame body is incomplete.");
        }

        byte[] body = bodyLength == 0
            ? EmptyBody
            : ReadBody(payload, bodyLength);

        return new CRpcMessage(header, body);
    }

    public void WriteTo(IByteBuffer output)
    {
        var payloadLength = CRpcMessageHeader.FixedLength + Body.Length;
        output.WriteInt(Magic);
        output.WriteInt(payloadLength);
        Header.WriteTo(output);
        if (Body.Length > 0)
        {
            output.WriteBytes(Body);
        }
    }

    public int GetFrameLength() => MinFrameLength + Body.Length;

    public CRpcMessage CreateResponse(int resultCode, byte[] body)
    {
        var responseHeader = Header.CreateResponse(resultCode, body ?? EmptyBody);
        return new CRpcMessage(responseHeader, body ?? EmptyBody);
    }

    private static byte[] ReadBody(IByteBuffer frame, int bodyLength)
    {
        var body = new byte[bodyLength];
        frame.ReadBytes(body);
        return body;
    }
}
