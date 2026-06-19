# CRpc v2 Codec Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the v1 CRpc binary wire codec with a fixed 22-byte header, `'CRPC'` magic, explicit `CrpcMessageType` routing, and no application-layer checksum — as a breaking change across server, client, gateway, and tests.

**Architecture:** Rewrite codec types in place (`CRpcMessage`, `CRpcMessageHeader`, encoder, decoder). Delete v1 `CRpcMessageState`, `ChecksumsUtil`, and `encryptAndCompress`. Route inbound messages by `MessageType` (Request / Response / Push) instead of state bitmasks. Reserve `flags` + `bodyOriginLen` for future compression; v2.0 always writes `flags = 0`.

**Tech Stack:** C# / .NET, DotNetty (`LengthFieldBasedFrameDecoder`, `MessageToByteEncoder`), xUnit, `dotnet test`.

**Spec reference:** `docs/superpowers/specs/2026-06-19-crpc-v2-codec-design.md`

**Repository rule:** Do not create commits unless the user explicitly requests them.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `CRpc/Rpc/CRpc/Codec/CrpcMessageType.cs` | New enum: `Request`, `Response`, `Push`. |
| `CRpc/Rpc/CRpc/Codec/CrpcFrameFlags.cs` | New reserved flag constants (`Compressed = 0x01`). |
| `CRpc/Rpc/CRpc/Codec/CRpcMessageHeader.cs` | Rewritten fixed 22-byte header with `ServiceId`, `MethodId`, `MessageType`. |
| `CRpc/Rpc/CRpc/Codec/CRpcMessage.cs` | Rewritten frame constants, `ReadFrom`/`WriteTo`, `CreateResponse`. |
| `CRpc/Rpc/CRpc/Codec/CRpcMessageEncoder.cs` | Parameterless encoder; no compress path. |
| `CRpc/Rpc/CRpc/Codec/CRpcMessageDecoder.cs` | v2 framing; no checksum validation. |
| `CRpc/Rpc/CRpc/Codec/CRpcMessageState.cs` | **Delete.** |
| `CRpc/Rpc/CRpc/Codec/ChecksumsUtil.cs` | **Delete.** |
| `CRpc/Rpc/CRpc/Server/CRpcServerOptions.cs` | Remove `HashLength`, `CompressThreshold`. |
| `CRpc/Rpc/CRpc/Client/CRpcClientOptions.cs` | Remove `HashLength`, `CompressThreshold`. |
| `CRpc/Rpc/CRpc/Server/CRpcServer.cs` | Wire parameterless encoder/decoder. |
| `CRpc/Rpc/CRpc/Client/CRpcClientPipelineFactory.cs` | Wire parameterless encoder/decoder. |
| `CRpc/Rpc/CRpc/Client/CRpcClient.cs` | Route by `MessageType`; build Request frames. |
| `CRpc/Rpc/CRpc/Server/CRpcConnection.cs` | Build Push frames with `MessageType.Push`. |
| `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs` | Ignore non-Request inbound messages. |
| `CRpc/Rpc/CRpc/Server/HttpServerHandler.cs` | Build internal Request messages with v2 header. |
| `Tests/CRPC.Tests/CRpcV2CodecTests.cs` | New focused wire round-trip and layout tests. |
| `Tests/CRPC.Tests/CRpcMessageEncoderTests.cs` | Rewrite for v2 magic and frame length. |
| `Tests/CRPC.Tests/CRpcClientTests.cs` | Update `CreatePush` / `CreateResponse` helpers. |
| `Tests/CRPC.Tests/CRpcServerHandlerTests.cs` | Update request builders and response assertions. |
| `Tests/CRPC.Tests/CRpcConnectionTests.cs` | Assert `MessageType.Push` instead of state flags. |
| `Tests/CRPC.Tests/GateWay/GateWayServerHandlerTests.cs` | Update helpers and `MessageType.Response` asserts. |
| `Tests/CRPC.Tests/RpcServiceInvokerTests.cs` | Update request builder. |
| `Tests/CRPC.Tests/CRpcTransportOptionsTests.cs` | Remove hash/compress default asserts. |
| `Doc/protocol.md` | Document v2 wire format summary. |

HTTP code generation and `IRpcHttpJsonCodec` are unchanged.

---

## Task 1: Wire Type Enums

**Files:**
- Create: `CRpc/Rpc/CRpc/Codec/CrpcMessageType.cs`
- Create: `CRpc/Rpc/CRpc/Codec/CrpcFrameFlags.cs`

- [ ] **Step 1: Add `CrpcMessageType`**

Create `CRpc/Rpc/CRpc/Codec/CrpcMessageType.cs`:

```csharp
namespace CRpc.Rpc.CRpc.Codec;

public enum CrpcMessageType : byte
{
    Request = 0,
    Response = 1,
    Push = 2,
}
```

- [ ] **Step 2: Add `CrpcFrameFlags`**

Create `CRpc/Rpc/CRpc/Codec/CrpcFrameFlags.cs`:

```csharp
namespace CRpc.Rpc.CRpc.Codec;

public static class CrpcFrameFlags
{
    public const byte None = 0;
    public const byte Compressed = 0x01;
}
```

- [ ] **Step 3: Build to verify compile**

Run:

```bash
dotnet build CRpc/CRPC.csproj
```

Expected: PASS (no test changes yet).

---

## Task 2: Fixed Header Read/Write (TDD)

**Files:**
- Modify: `CRpc/Rpc/CRpc/Codec/CRpcMessageHeader.cs` (full rewrite)
- Create: `Tests/CRPC.Tests/CRpcV2CodecTests.cs`

- [ ] **Step 1: Write failing header layout test**

Create `Tests/CRPC.Tests/CRpcV2CodecTests.cs`:

```csharp
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Buffers;

namespace CRPC.Tests;

public class CRpcV2CodecTests
{
    [Fact]
    public void HeaderWriteProducesTwentyTwoBytesWithExpectedOffsets()
    {
        var header = CRpcMessageHeader.Create(
            CrpcMessageType.Request,
            serviceId: 7,
            methodId: 3,
            reqSequence: 42,
            resultCode: 0,
            body: new byte[] { 1, 2, 3 });

        var buffer = Unpooled.Buffer();
        header.WriteTo(buffer);

        try
        {
            Assert.Equal(CRpcMessageHeader.FixedLength, buffer.ReadableBytes);
            Assert.Equal(1, buffer.ReadByte());                          // version
            Assert.Equal((byte)CrpcMessageType.Request, buffer.ReadByte());
            Assert.Equal(CrpcFrameFlags.None, buffer.ReadByte());        // flags
            Assert.Equal(0, buffer.ReadByte());                          // reserved
            Assert.Equal(7, buffer.ReadUnsignedShort());                 // serviceId
            Assert.Equal(3, buffer.ReadUnsignedShort());                 // methodId
            Assert.Equal(42L, buffer.ReadLong());                        // reqSeq
            Assert.Equal(0, buffer.ReadInt());                           // resultCode
            Assert.Equal(3, buffer.ReadInt());                             // bodyOriginLen
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void HeaderRoundTripPreservesFields()
    {
        var original = CRpcMessageHeader.Create(
            CrpcMessageType.Push,
            serviceId: 1000,
            methodId: 2,
            reqSequence: 0,
            resultCode: 0,
            body: new byte[] { 9 });

        var buffer = Unpooled.Buffer();
        original.WriteTo(buffer);

        try
        {
            var restored = CRpcMessageHeader.ReadFrom(buffer);
            Assert.Equal(original.MessageType, restored.MessageType);
            Assert.Equal(original.ServiceId, restored.ServiceId);
            Assert.Equal(original.MethodId, restored.MethodId);
            Assert.Equal(original.ReqSequence, restored.ReqSequence);
            Assert.Equal(original.ResultCode, restored.ResultCode);
            Assert.Equal(original.BodyOriginLen, restored.BodyOriginLen);
        }
        finally
        {
            buffer.Release();
        }
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcV2CodecTests
```

Expected: FAIL — `Create` / `ReadFrom` / `FixedLength` do not exist on `CRpcMessageHeader`.

- [ ] **Step 3: Rewrite `CRpcMessageHeader`**

Replace `CRpc/Rpc/CRpc/Codec/CRpcMessageHeader.cs` with:

```csharp
using DotNetty.Buffers;

namespace CRpc.Rpc.CRpc.Codec;

public sealed class CRpcMessageHeader
{
    public const int FixedLength = 22;
    public const byte ProtocolVersion = 1;

    public byte Version { get; init; } = ProtocolVersion;
    public CrpcMessageType MessageType { get; init; }
    public byte Flags { get; init; }
    public byte Reserved { get; init; }
    public ushort ServiceId { get; init; }
    public ushort MethodId { get; init; }
    public long ReqSequence { get; init; }
    public int ResultCode { get; init; }
    public int BodyOriginLen { get; init; }

    public static CRpcMessageHeader Create(
        CrpcMessageType messageType,
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
            Flags = CrpcFrameFlags.None,
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

        var messageType = (CrpcMessageType)buffer.ReadByte();
        if (messageType > CrpcMessageType.Push)
        {
            throw new InvalidDataException($"Unsupported CRpc message type {(byte)messageType}.");
        }

        var flags = buffer.ReadByte();
        if (flags != CrpcFrameFlags.None)
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
            MessageType = CrpcMessageType.Response,
            Flags = CrpcFrameFlags.None,
            ServiceId = ServiceId,
            MethodId = MethodId,
            ReqSequence = ReqSequence,
            ResultCode = resultCode,
            BodyOriginLen = body.Length,
        };
    }
}
```

- [ ] **Step 4: Run header tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcV2CodecTests
```

Expected: PASS for the two header tests (frame tests not added yet).

---

## Task 3: `CRpcMessage` Frame Encode/Decode (TDD)

**Files:**
- Modify: `CRpc/Rpc/CRpc/Codec/CRpcMessage.cs` (full rewrite)
- Modify: `Tests/CRPC.Tests/CRpcV2CodecTests.cs`

- [ ] **Step 1: Write failing frame tests**

Append to `Tests/CRPC.Tests/CRpcV2CodecTests.cs`:

```csharp
    [Fact]
    public void ToFrameWritesCrpcMagicAndPayloadLength()
    {
        var message = CRpcMessage.Create(
            CrpcMessageType.Request,
            serviceId: 7,
            methodId: 3,
            reqSequence: 42,
            resultCode: 0,
            body: new byte[] { 1, 2, 3 });

        var buffer = Unpooled.Buffer();
        message.WriteTo(buffer);

        try
        {
            Assert.Equal(CRpcMessage.MinFrameLength + 3, buffer.ReadableBytes);
            Assert.Equal(CRpcMessage.Magic, buffer.ReadInt());
            Assert.Equal(CRpcMessageHeader.FixedLength + 3, buffer.ReadInt());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void ReadFromFrameRoundTripsRequest()
    {
        var original = CRpcMessage.Create(
            CrpcMessageType.Request,
            serviceId: 1,
            methodId: 2,
            reqSequence: 99,
            resultCode: 0,
            body: new byte[] { 5, 6 });

        var buffer = Unpooled.Buffer();
        original.WriteTo(buffer);

        try
        {
            var restored = CRpcMessage.ReadFrom(buffer);
            Assert.Equal(original.Header.MessageType, restored.Header.MessageType);
            Assert.Equal(original.ServiceId, restored.ServiceId);
            Assert.Equal(original.MethodId, restored.MethodId);
            Assert.Equal(original.ReqSequence, restored.ReqSequence);
            Assert.Equal(original.Body, restored.Body);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void CreateResponseSetsMessageTypeResponse()
    {
        var request = CRpcMessage.Create(
            CrpcMessageType.Request,
            serviceId: 10,
            methodId: 20,
            reqSequence: 5,
            resultCode: 0,
            body: Array.Empty<byte>());

        var response = request.CreateResponse(resultCode: 0, body: new byte[] { 7 });

        Assert.Equal(CrpcMessageType.Response, response.Header.MessageType);
        Assert.Equal(5L, response.ReqSequence);
        Assert.Equal(10, response.ServiceId);
        Assert.Equal(20, response.MethodId);
        Assert.Equal(new byte[] { 7 }, response.Body);
    }
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcV2CodecTests
```

Expected: FAIL — `CRpcMessage.Create` / `WriteTo` / `ReadFrom` missing.

- [ ] **Step 3: Rewrite `CRpcMessage`**

Replace `CRpc/Rpc/CRpc/Codec/CRpcMessage.cs` with:

```csharp
using DotNetty.Buffers;

namespace CRpc.Rpc.CRpc.Codec;

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
    public CrpcMessageType MessageType => Header.MessageType;

    public static CRpcMessage Create(
        CrpcMessageType messageType,
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

        var header = CRpcMessageHeader.ReadFrom(frame);
        var bodyLength = payloadLength - CRpcMessageHeader.FixedLength;
        if (frame.ReadableBytes < bodyLength)
        {
            throw new InvalidDataException("CRpc frame body is incomplete.");
        }

        byte[] body = bodyLength == 0
            ? EmptyBody
            : ReadBody(frame, bodyLength);

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

    // Temporary shims for incremental migration; remove after Task 8.
    public CRpcMessageHeader getHeader() => Header;
    public ushort getServiceId() => ServiceId;
    public ushort getMethodId() => MethodId;
    public long getReqSequence() => ReqSequence;
    public byte[] getBody() => Body;
    public void setBody(byte[] bytes) => Body = bytes ?? EmptyBody;
    public CRpcMessage createResponse(int resultCode, byte[] bytes) => CreateResponse(resultCode, bytes);

    private static byte[] ReadBody(IByteBuffer frame, int bodyLength)
    {
        var body = new byte[bodyLength];
        frame.ReadBytes(body);
        return body;
    }
}
```

- [ ] **Step 4: Run frame tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcV2CodecTests
```

Expected: PASS for all tests in `CRpcV2CodecTests`.

---

## Task 4: Encoder and Decoder Handlers

**Files:**
- Modify: `CRpc/Rpc/CRpc/Codec/CRpcMessageEncoder.cs`
- Modify: `CRpc/Rpc/CRpc/Codec/CRpcMessageDecoder.cs`
- Modify: `Tests/CRPC.Tests/CRpcMessageEncoderTests.cs`

- [ ] **Step 1: Rewrite encoder tests**

Replace `Tests/CRPC.Tests/CRpcMessageEncoderTests.cs` with:

```csharp
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Buffers;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class CRpcMessageEncoderTests
{
    [Fact]
    public void EncodeWritesCrpcMagicAndPayloadLength()
    {
        var encoder = new CRpcMessageEncoder();
        var channel = new EmbeddedChannel(encoder);
        var message = CRpcMessage.Create(
            CrpcMessageType.Request,
            serviceId: 7,
            methodId: 3,
            reqSequence: 42,
            resultCode: 0,
            body: new byte[] { 1, 2, 3 });

        Assert.True(channel.WriteOutbound(message));

        var frame = channel.ReadOutbound<IByteBuffer>();
        Assert.NotNull(frame);
        try
        {
            Assert.Equal(CRpcMessage.Magic, frame.GetInt(frame.ReaderIndex));
            Assert.Equal(
                CRpcMessageHeader.FixedLength + 3,
                frame.GetInt(frame.ReaderIndex + sizeof(int)));
        }
        finally
        {
            frame.Release();
        }
    }

    [Fact]
    public void EncodeProducesExactFrameLength()
    {
        var encoder = new CRpcMessageEncoder();
        var channel = new EmbeddedChannel(encoder);
        var message = CRpcMessage.Create(
            CrpcMessageType.Request,
            serviceId: 1,
            methodId: 1,
            reqSequence: 1,
            resultCode: 0,
            body: new byte[] { 9, 8, 7 });

        Assert.True(channel.WriteOutbound(message));

        var frame = channel.ReadOutbound<IByteBuffer>();
        Assert.NotNull(frame);
        try
        {
            Assert.Equal(message.GetFrameLength(), frame.ReadableBytes);
        }
        finally
        {
            frame.Release();
        }
    }
}
```

- [ ] **Step 2: Run encoder tests to verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcMessageEncoderTests
```

Expected: FAIL — encoder constructor still requires v1 parameters.

- [ ] **Step 3: Rewrite encoder**

Replace `CRpc/Rpc/CRpc/Codec/CRpcMessageEncoder.cs` with:

```csharp
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Codec;

public sealed class CRpcMessageEncoder : MessageToByteEncoder<CRpcMessage>
{
    protected override void Encode(IChannelHandlerContext context, CRpcMessage message, IByteBuffer output)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.WriteTo(output);
    }
}
```

- [ ] **Step 4: Rewrite decoder**

Replace `CRpc/Rpc/CRpc/Codec/CRpcMessageDecoder.cs` with:

```csharp
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Codec;

public sealed class CRpcMessageDecoder : LengthFieldBasedFrameDecoder
{
    public CRpcMessageDecoder(int maxFrameLength)
        : base(maxFrameLength, lengthFieldOffset: 4, lengthFieldLength: 4, lengthAdjustment: 0, initialBytesToStrip: 8)
    {
    }

    protected override object Decode(IChannelHandlerContext context, IByteBuffer input)
    {
        if (input.ReadableBytes < CRpcMessage.FramePrefixLength)
        {
            return null;
        }

        IByteBuffer frame = null;
        try
        {
            frame = (IByteBuffer)base.Decode(context, input);
            if (frame is null)
            {
                return null;
            }

            return CRpcMessage.ReadFrom(frame);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{context.Channel} CRpc decode failed, closing connection: {exception.Message}");
            _ = context.CloseAsync();
            return null;
        }
        finally
        {
            if (frame is not null)
            {
                ReferenceCountUtil.Release(frame);
            }
        }
    }
}
```

- [ ] **Step 5: Run encoder tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "CRPC.Tests.CRpcMessageEncoderTests|CRPC.Tests.CRpcV2CodecTests"
```

Expected: PASS.

---

## Task 5: Delete v1 Dead Code

**Files:**
- Delete: `CRpc/Rpc/CRpc/Codec/CRpcMessageState.cs`
- Delete: `CRpc/Rpc/CRpc/Codec/ChecksumsUtil.cs`

- [ ] **Step 1: Delete v1 codec helpers**

Remove both files listed above.

- [ ] **Step 2: Build and surface remaining references**

Run:

```bash
dotnet build Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: FAIL with compile errors referencing `CRpcMessageState`, `valueOf`, `hasState`, `addState`, `module`, `command`, `MAGIC_NUM`, `getSize`, `HashLength`, `CompressThreshold`.

These are fixed in Tasks 6–8.

---

## Task 6: Transport Options Cleanup

**Files:**
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServerOptions.cs`
- Modify: `CRpc/Rpc/CRpc/Client/CRpcClientOptions.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServer.cs`
- Modify: `CRpc/Rpc/CRpc/Client/CRpcClientPipelineFactory.cs`
- Modify: `Tests/CRPC.Tests/CRpcTransportOptionsTests.cs`

- [ ] **Step 1: Remove hash/compress options**

In `CRpcServerOptions.cs`, delete:

- `DefaultHashLength`, `DefaultCompressThreshold`
- `HashLength`, `CompressThreshold` properties

In `CRpcClientOptions.cs`, delete the same four members.

- [ ] **Step 2: Wire parameterless codec handlers**

In `CRpcServer.cs` child pipeline:

```csharp
pipeline.AddLast("decoder", new CRpcMessageDecoder(startOptions.MaxFrameLength));
pipeline.AddLast("encoder", new CRpcMessageEncoder());
```

In `CRpcClientPipelineFactory.cs`:

```csharp
pipeline.AddLast("decoder", new CRpcMessageDecoder(options.MaxFrameLength));
pipeline.AddLast("encoder", new CRpcMessageEncoder());
```

- [ ] **Step 3: Update transport option tests**

Replace hash/compress assertions in `CRpcTransportOptionsTests.cs`:

```csharp
    [Fact]
    public void CRpcServerOptionsDefaultsMatchExpectedValues()
    {
        var options = new CRpcServerOptions();

        Assert.Equal(CRpcServerOptions.DefaultPort, options.Port);
        Assert.Equal(CRpcServerOptions.DefaultMaxFrameLength, options.MaxFrameLength);
        Assert.Equal(CRpcServerOptions.DefaultBossThreadCount, options.BossThreadCount);
        Assert.Equal(CRpcServerOptions.DefaultWorkerThreadCount, options.WorkerThreadCount);
        Assert.Equal(CRpcServerOptions.DefaultSoBacklog, options.SoBacklog);
    }

    [Fact]
    public void CRpcClientOptionsDefaultsMatchExpectedValues()
    {
        var options = new CRpcClientOptions();

        Assert.Equal(CRpcClientOptions.DefaultIoThreadCount, options.IoThreadCount);
        Assert.Equal(CRpcClientOptions.DefaultConnectTimeoutSeconds, options.ConnectTimeoutSeconds);
        Assert.Equal(CRpcClientOptions.DefaultHeartbeatIdleSeconds, options.HeartbeatIdleSeconds);
        Assert.Equal(CRpcClientOptions.DefaultMaxFrameLength, options.MaxFrameLength);
        Assert.Equal(CRpcClientOptions.DefaultCallTimeoutMilliseconds, options.CallTimeoutMilliseconds);
    }
```

Remove `CRpcClientExposesConfiguredOptions` hash/compress variant or change it to:

```csharp
var configured = new CRpcClientOptions { MaxFrameLength = 1024 * 1024 };
```

- [ ] **Step 4: Build transport layer**

Run:

```bash
dotnet build CRpc/CRPC.csproj
```

Expected: PASS for CRpc project; test project still fails until Task 8.

---

## Task 7: Shared Test Helper

**Files:**
- Create: `Tests/CRPC.Tests/CrpcTestMessages.cs`

- [ ] **Step 1: Add centralized v2 message builders**

Create `Tests/CRPC.Tests/CrpcTestMessages.cs`:

```csharp
using CRpc.Rpc.CRpc.Codec;

namespace CRPC.Tests;

internal static class CrpcTestMessages
{
    public static CRpcMessage CreateRequest(
        ushort serviceId,
        ushort methodId = 1,
        long reqSequence = 1,
        byte[]? body = null)
    {
        return CRpcMessage.Create(
            CrpcMessageType.Request,
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
            CrpcMessageType.Response,
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
            CrpcMessageType.Push,
            serviceId,
            methodId,
            reqSequence: 0,
            resultCode: 0,
            body: body ?? Array.Empty<byte>());
    }
}
```

---

## Task 8: Client, Server, and HTTP Path Migration

**Files:**
- Modify: `CRpc/Rpc/CRpc/Client/CRpcClient.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcConnection.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs`
- Modify: `CRpc/Rpc/CRpc/Server/HttpServerHandler.cs`

- [ ] **Step 1: Update client inbound routing**

In `CRpcClient.CompleteReceiveResponse`, replace state checks with:

```csharp
private void CompleteReceiveResponse(CRpcMessage message)
{
    switch (message.MessageType)
    {
        case CrpcMessageType.Push:
            DispatchPush(message);
            return;
        case CrpcMessageType.Response:
            CompletePendingCall(message);
            return;
        default:
            Console.WriteLine(
                $"CRpcClient ignored inbound message type {message.MessageType}: serviceId={message.ServiceId}, methodId={message.MethodId}");
            return;
    }
}

private void CompletePendingCall(CRpcMessage message)
{
    var reqSequence = message.ReqSequence;
    if (results.Remove(reqSequence, out var pendingCall))
    {
        pendingCall.TimeoutTimer?.Cancel();
        pendingCall.Source.TrySetResult(message);
    }
}
```

- [ ] **Step 2: Update client outbound request builder**

In `CRpcClient.__Send`, replace header construction with:

```csharp
var req = CRpcMessage.Create(
    CrpcMessageType.Request,
    serviceId,
    methodId,
    reqSeq,
    resultCode: 0,
    bytes);
```

- [ ] **Step 3: Update server push sender**

In `CRpcConnection.SendPushAsync`, replace header construction with:

```csharp
var message = CRpcMessage.Create(
    CrpcMessageType.Push,
    serviceId,
    methodId,
    reqSequence: 0,
    resultCode: 0,
    body);
```

- [ ] **Step 4: Ignore non-request frames on server**

At the top of `CRpcServerHandler.ChannelRead`, after casting `msg`:

```csharp
if (message.MessageType != CrpcMessageType.Request)
{
    Console.WriteLine(
        $"CRpcServerHandler ignored inbound message type {message.MessageType}: serviceId={message.ServiceId}, methodId={message.MethodId}");
    return;
}
```

- [ ] **Step 5: Update HTTP internal request builder**

In `HttpServerHandler` where it builds a `CRpcMessage` for loop dispatch, replace v1 header code with:

```csharp
var message = CRpcMessage.Create(
    CrpcMessageType.Request,
    serviceId,
    methodId,
    sn,
    resultCode: 0,
    body);
```

- [ ] **Step 6: Build CRpc library**

Run:

```bash
dotnet build CRpc/CRPC.csproj
```

Expected: PASS.

---

## Task 9: Test Suite Migration

**Files:**
- Modify: `Tests/CRPC.Tests/CRpcClientTests.cs`
- Modify: `Tests/CRPC.Tests/CRpcServerHandlerTests.cs`
- Modify: `Tests/CRPC.Tests/CRpcConnectionTests.cs`
- Modify: `Tests/CRPC.Tests/GateWay/GateWayServerHandlerTests.cs`
- Modify: `Tests/CRPC.Tests/RpcServiceInvokerTests.cs`
- Modify: `Tests/CRPC.Tests/CRpcServerHandlerTests.cs` (pipeline factory args)
- Modify: any other test files surfaced by `dotnet build`

- [ ] **Step 1: Replace v1 helpers in `CRpcClientTests`**

Delete local `CreatePush` / `CreateResponse` and use `CrpcTestMessages` instead.

Update push/response tests:

```csharp
client.OnReceiveResponse(CrpcTestMessages.CreatePush(1, 2, Array.Empty<byte>()));
// ...
client.OnReceiveResponse(CrpcTestMessages.CreateResponse(7, 8, reqSequence: 42));
```

- [ ] **Step 2: Update `CRpcServerHandlerTests`**

Replace `CreateRequest` with:

```csharp
private static CRpcMessage CreateRequest(ushort serviceId)
{
    return CrpcTestMessages.CreateRequest(serviceId, methodId: 1, reqSequence: 1);
}
```

Replace pipeline setup — remove hash/compress constructor args:

```csharp
new EmbeddedChannel(
    new CRpcMessageDecoder(CRpcServerOptions.DefaultMaxFrameLength),
    new CRpcMessageEncoder(),
    handler);
```

Replace response assertions:

```csharp
Assert.Equal(CrpcMessageType.Response, response.MessageType);
```

Remove `hasState(STATE_RESPONSE)` / `NONE_ENCRYPT` asserts.

- [ ] **Step 3: Update `CRpcConnectionTests`**

```csharp
Assert.Equal(CrpcMessageType.Push, outbound.MessageType);
```

Remove all `hasState` assertions.

- [ ] **Step 4: Update gateway tests**

In `GateWayServerHandlerTests.cs`:

- Use `CrpcTestMessages.CreateRequest(serviceId)` in helpers.
- Pipeline uses parameterless encoder and single-arg decoder.
- Assert `response.MessageType == CrpcMessageType.Response`.

- [ ] **Step 5: Update `RpcServiceInvokerTests`**

```csharp
var request = CrpcTestMessages.CreateRequest(serviceId, methodId, reqSequence: 1);
var response = RpcServiceInvoker.BuildCrpcResponse(request, code: 0, body: expectedBody);
Assert.Equal(CrpcMessageType.Response, response.MessageType);
```

- [ ] **Step 6: Fix any remaining compile errors**

Run:

```bash
dotnet build Tests/CRPC.Tests/CRPC.Tests.csproj
```

Fix any remaining references to:

- `CRpcMessageState`
- `CRpcMessageHeader.valueOf(...)`
- `module:` / `command:`
- `addState(...)`
- `hasState(...)`
- `MAGIC_NUM`
- `getSize()`
- `encryptAndCompress(...)`

- [ ] **Step 7: Run full test suite**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: PASS (all tests).

---

## Task 10: Remove Migration Shims from `CRpcMessage`

**Files:**
- Modify: `CRpc/Rpc/CRpc/Codec/CRpcMessage.cs`
- Modify: all call sites still using `getHeader()`, `getServiceId()`, `getBody()`, `createResponse()`

- [ ] **Step 1: Convert call sites to PascalCase API**

Replace across `CRpc/` and `Tests/`:

| Old | New |
| --- | --- |
| `getHeader()` | `Header` |
| `getServiceId()` | `ServiceId` |
| `getMethodId()` | `MethodId` |
| `getReqSequence()` | `ReqSequence` |
| `getBody()` | `Body` |
| `setBody(bytes)` | assign `Body` via `CreateResponse` / new instance |
| `createResponse(code, body)` | `CreateResponse(code, body)` |

- [ ] **Step 2: Delete shim methods**

Remove the `// Temporary shims` block from `CRpcMessage.cs`.

- [ ] **Step 3: Re-run full tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: PASS.

---

## Task 11: Protocol Documentation

**Files:**
- Modify: `Doc/protocol.md`

- [ ] **Step 1: Write v2 protocol summary**

Replace `Doc/protocol.md` with:

```markdown
# CRpc Binary Protocol (v2)

Full design: `docs/superpowers/specs/2026-06-19-crpc-v2-codec-design.md`

## Frame

```text
magic(4) + payloadLen(4) + header(22) + body(N)
```

- `magic` = `0x43525043` (`'CRPC'`, little-endian)
- `payloadLen` = `22 + body.Length` (header + body only)
- All integers little-endian

## Header (22 bytes)

| Offset | Field |
| --- | --- |
| 0 | `version` = 1 |
| 1 | `messageType` — 0 Request, 1 Response, 2 Push |
| 2 | `flags` — 0 in v2.0 (`0x01` = compressed, reserved) |
| 3 | `reserved` = 0 |
| 4 | `serviceId` u16 |
| 6 | `methodId` u16 |
| 8 | `reqSeq` u64 |
| 16 | `resultCode` i32 |
| 20 | `bodyOriginLen` u32 |

## Body

Raw protobuf bytes. No application-layer checksum in v2.0.

## Message conventions

| Type | reqSeq | resultCode |
| --- | --- | --- |
| Request | > 0 | 0 |
| Response | matches request | service result |
| Push | 0 | 0 |
```

- [ ] **Step 2: Verify doc renders**

Open `Doc/protocol.md` and confirm the frame diagram and field table match the spec.

---

## Task 12: Final Verification

- [ ] **Step 1: Run full test suite**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: all tests PASS.

- [ ] **Step 2: Build HelloWorld and Gateway examples**

```bash
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj
dotnet build Example/HelloWorld/Client/HelloWorldClient.csproj
dotnet build Example/GateWay/GateWayServer/GateWayServer.csproj
```

Expected: PASS with no references to deleted v1 codec symbols.

- [ ] **Step 3: Manual smoke (optional)**

1. Start HelloWorld server.
2. Run HelloWorld client `SayHello` RPC.
3. If push is configured in HelloWorld, verify server push still arrives.

---

## Spec Coverage Self-Review

| Spec requirement | Task |
| --- | --- |
| Breaking change, no v1 compat | Tasks 3–5 |
| Fixed 22-byte header | Tasks 2–3 |
| `messageType` enum routing | Tasks 1, 8 |
| `serviceId` / `methodId` naming | Tasks 2–3, 8, 10 |
| No checksum | Tasks 4–5 |
| Compression reserved (`flags`, `bodyOriginLen`) | Tasks 2–3 |
| Encoder/decoder DotNetty params | Task 4, 6 |
| Client Response/Push routing | Task 8 |
| Server Request-only dispatch | Task 8 |
| Gateway/tests migration | Task 9 |
| `Doc/protocol.md` | Task 11 |

No placeholder steps remain. Type names (`CrpcMessageType`, `CRpcMessage.Create`, `MessageType` property) are consistent across tasks.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-19-crpc-v2-codec.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — implement tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
