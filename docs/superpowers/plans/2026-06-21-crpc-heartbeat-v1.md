# CRpc Heartbeat v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Client sends `Heartbeat` every 15s (writer idle); server closes connections after 45s with no inbound data (reader idle); no server ack; Gateway backend reacts via `ConnectionLost`.

**Architecture:** Extend binary codec with `CRpcMessageType.Heartbeat`. Add `CRpcClientHeartbeatHandler` and `CRpcServerReadIdleHandler` to DotNetty pipelines via client/server pipeline factories. Server handlers short-circuit Heartbeat without dispatching to `CRpcLoop`. `CRpcClient.ConnectionLost` exposes disconnect to Gateway; no generic auto-reconnect.

**Tech Stack:** C# / .NET, xUnit, DotNetty (`IdleStateHandler`, `IdleStateEvent`), `CRpcTask` / `CRpcLoop`.

**User Constraint:** Do not commit or merge automatically. Implementation tasks intentionally omit git commit steps.

---

## Spec Reference

Design spec: `docs/superpowers/specs/2026-06-21-crpc-heartbeat-v1-design.md`

## File Structure

| File | Responsibility |
|------|----------------|
| `CRpc/Rpc/CRpc/Codec/CRpcMessageType.cs` | Add `Heartbeat = 3`, document `HeartbeatAck = 4` reserved |
| `CRpc/Rpc/CRpc/Codec/CRpcMessageHeader.cs` | Allow decode of type `Heartbeat` |
| `CRpc/Rpc/CRpc/Codec/CRpcMessage.cs` | `CreateHeartbeat()` factory |
| `CRpc/Rpc/CRpc/Client/CRpcClientOptions.cs` | `HeartbeatEnabled`, `HeartbeatIntervalSeconds` (default 15) |
| `CRpc/Rpc/CRpc/Server/CRpcServerOptions.cs` | `HeartbeatEnabled`, `ReadIdleSeconds` (default 45) |
| `CRpc/Rpc/CRpc/Client/CRpcClientHeartbeatHandler.cs` | Writer-idle → send Heartbeat frame |
| `CRpc/Rpc/CRpc/Server/CRpcServerReadIdleHandler.cs` | Reader-idle → `CloseAsync` |
| `CRpc/Rpc/CRpc/Server/CRpcServerPipelineFactory.cs` | Shared server pipeline wiring |
| `CRpc/Rpc/CRpc/Client/CRpcClientPipelineFactory.cs` | Writer idle + heartbeat handler |
| `CRpc/Rpc/CRpc/Client/CRpcClient.cs` | `ConnectionLost` event; ignore inbound Heartbeat |
| `CRpc/Rpc/CRpc/Server/CRpcServer.cs` | Use `CRpcServerPipelineFactory` |
| `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs` | Short-circuit Heartbeat |
| `Example/GateWay/GateWay.Core/GateWayServerHandler.cs` | Short-circuit Heartbeat |
| `Example/GateWay/GateWay.Core/GateWaySessionTable.cs` | Backend `ConnectionLost` → remove link + unhealthy |
| `Example/HelloWorld/Server/Http/UnifiedServer.cs` | Use `CRpcServerPipelineFactory` for CRpc branch |
| `Doc/protocol.md` | Document Heartbeat type |
| `Tests/CRPC.Tests/CRpcCodecTests.cs` | Heartbeat encode/decode tests |
| `Tests/CRPC.Tests/CRpcTransportOptionsTests.cs` | Updated defaults + validation |
| `Tests/CRPC.Tests/CRpcClientPipelineFactoryTests.cs` | Heartbeat handler + disabled mode |
| `Tests/CRPC.Tests/CRpcServerPipelineFactoryTests.cs` | New server pipeline tests |
| `Tests/CRPC.Tests/CRpcClientHeartbeatHandlerTests.cs` | Writer-idle send test |
| `Tests/CRPC.Tests/CRpcServerReadIdleHandlerTests.cs` | Reader-idle close test |
| `Tests/CRPC.Tests/CRpcServerHandlerTests.cs` | Heartbeat not dispatched |
| `Tests/CRPC.Tests/CRpcClientTests.cs` | `ConnectionLost` + ignore inbound Heartbeat |
| `Tests/CRPC.Tests/GateWay/GateWaySessionTableTests.cs` | Backend `ConnectionLost` cleanup |

**Task order:** 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9

---

## Task 1: Protocol — Heartbeat message type and factory

**Files:**
- Modify: `CRpc/Rpc/CRpc/Codec/CRpcMessageType.cs`
- Modify: `CRpc/Rpc/CRpc/Codec/CRpcMessageHeader.cs`
- Modify: `CRpc/Rpc/CRpc/Codec/CRpcMessage.cs`
- Modify: `Tests/CRPC.Tests/CRpcCodecTests.cs`

- [ ] **Step 1: Write failing codec tests**

Add to `CRpcCodecTests.cs`:

```csharp
[Fact]
public void CreateHeartbeatProducesExpectedHeaderFields()
{
    var message = CRpcMessage.CreateHeartbeat();

    Assert.Equal(CRpcMessageType.Heartbeat, message.MessageType);
    Assert.Equal(0, message.ServiceId);
    Assert.Equal(0, message.MethodId);
    Assert.Equal(0, message.ReqSequence);
    Assert.Equal(0, message.ResultCode);
    Assert.Empty(message.Body);
}

[Fact]
public void HeartbeatRoundTripThroughEncoderDecoder()
{
    var original = CRpcMessage.CreateHeartbeat();
    var encoder = new CRpcMessageEncoder();
    var decoder = new CRpcMessageDecoder(maxFrameLength: 1024);
    var encodeChannel = new EmbeddedChannel(encoder);
    encodeChannel.WriteOutbound(original);
    var frame = encodeChannel.ReadOutbound<IByteBuffer>();
    Assert.NotNull(frame);

    var decodeChannel = new EmbeddedChannel(decoder);
    decodeChannel.WriteInbound(frame.Retain());
    var decoded = decodeChannel.ReadInbound<CRpcMessage>();

    Assert.Equal(CRpcMessageType.Heartbeat, decoded.MessageType);
    Assert.Equal(0, decoded.ServiceId);
    Assert.Equal(0, decoded.MethodId);
    Assert.Equal(0, decoded.ReqSequence);
}

[Fact]
public void HeaderReadFromRejectsUnknownMessageTypeAboveHeartbeat()
{
    var header = CRpcMessageHeader.Create(
        CRpcMessageType.Request, 0, 0, 0, 0, Array.Empty<byte>());
    var buffer = Unpooled.Buffer();
    header.WriteTo(buffer);
    buffer.SetByte(1, 5); // invalid type above Heartbeat

    Assert.Throws<InvalidDataException>(() => CRpcMessageHeader.ReadFrom(buffer));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcCodecTests" -v q
```

Expected: FAIL — `CreateHeartbeat` missing; type 3 rejected by header validation.

- [ ] **Step 3: Implement protocol changes**

In `CRpcMessageType.cs`:

```csharp
public enum CRpcMessageType : byte
{
    Request = 0,
    Response = 1,
    Push = 2,
    Heartbeat = 3,
    // HeartbeatAck = 4 reserved for Phase 2; not implemented in v1
}
```

In `CRpcMessageHeader.cs`, change validation:

```csharp
if (messageType > CRpcMessageType.Heartbeat)
{
    throw new InvalidDataException($"Unsupported CRpc message type {(byte)messageType}.");
}
```

In `CRpcMessage.cs`:

```csharp
public static CRpcMessage CreateHeartbeat()
{
    return Create(
        CRpcMessageType.Heartbeat,
        serviceId: 0,
        methodId: 0,
        reqSequence: 0,
        resultCode: 0,
        Array.Empty<byte>());
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcCodecTests" -v q
```

Expected: PASS

---

## Task 2: Options — client/server heartbeat settings and validation

**Files:**
- Modify: `CRpc/Rpc/CRpc/Client/CRpcClientOptions.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServerOptions.cs`
- Modify: `Tests/CRPC.Tests/CRpcTransportOptionsTests.cs`

- [ ] **Step 1: Write failing options tests**

Replace `HeartbeatIdleSeconds` assertions and add validation tests in `CRpcTransportOptionsTests.cs`:

```csharp
[Fact]
public void CRpcClientOptionsDefaultsMatchExpectedValues()
{
    var options = new CRpcClientOptions();

    Assert.Equal(CRpcClientOptions.DefaultIoThreadCount, options.IoThreadCount);
    Assert.Equal(CRpcClientOptions.DefaultConnectTimeoutSeconds, options.ConnectTimeoutSeconds);
    Assert.True(options.HeartbeatEnabled);
    Assert.Equal(CRpcClientOptions.DefaultHeartbeatIntervalSeconds, options.HeartbeatIntervalSeconds);
    Assert.Equal(CRpcClientOptions.DefaultMaxFrameLength, options.MaxFrameLength);
    Assert.Equal(CRpcClientOptions.DefaultCallTimeoutMilliseconds, options.CallTimeoutMilliseconds);
}

[Fact]
public void CRpcServerOptionsDefaultsIncludeHeartbeatSettings()
{
    var options = new CRpcServerOptions();

    Assert.True(options.HeartbeatEnabled);
    Assert.Equal(CRpcServerOptions.DefaultReadIdleSeconds, options.ReadIdleSeconds);
}

[Fact]
public void CRpcClientOptionsValidateRejectsNonPositiveInterval()
{
    var options = new CRpcClientOptions { HeartbeatIntervalSeconds = 0 };
    Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
}

[Fact]
public void CRpcServerOptionsValidateRequiresReadIdleAtLeastTwiceClientInterval()
{
    var options = new CRpcServerOptions { ReadIdleSeconds = 10 };
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        options.Validate(clientHeartbeatIntervalSeconds: 15));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcTransportOptionsTests" -v q
```

Expected: FAIL — missing properties / `Validate`.

- [ ] **Step 3: Implement options**

In `CRpcClientOptions.cs`:

```csharp
public const int DefaultHeartbeatIntervalSeconds = 15;

public bool HeartbeatEnabled { get; init; } = true;

public int HeartbeatIntervalSeconds { get; init; } = DefaultHeartbeatIntervalSeconds;

public void Validate()
{
    if (HeartbeatIntervalSeconds <= 0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(HeartbeatIntervalSeconds),
            HeartbeatIntervalSeconds,
            "Heartbeat interval must be positive.");
    }
}
```

Remove `DefaultHeartbeatIdleSeconds` and `HeartbeatIdleSeconds`.

In `CRpcServerOptions.cs`:

```csharp
public const int DefaultReadIdleSeconds = 45;

public bool HeartbeatEnabled { get; init; } = true;

public int ReadIdleSeconds { get; init; } = DefaultReadIdleSeconds;

public void Validate(int clientHeartbeatIntervalSeconds = CRpcClientOptions.DefaultHeartbeatIntervalSeconds)
{
    if (ReadIdleSeconds <= 0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(ReadIdleSeconds),
            ReadIdleSeconds,
            "Read idle must be positive.");
    }

    if (ReadIdleSeconds < clientHeartbeatIntervalSeconds * 2)
    {
        throw new ArgumentOutOfRangeException(
            nameof(ReadIdleSeconds),
            ReadIdleSeconds,
            "Read idle must be at least twice the client heartbeat interval.");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcTransportOptionsTests" -v q
```

Expected: PASS

---

## Task 3: Client heartbeat handler and pipeline factory

**Files:**
- Create: `CRpc/Rpc/CRpc/Client/CRpcClientHeartbeatHandler.cs`
- Modify: `CRpc/Rpc/CRpc/Client/CRpcClientPipelineFactory.cs`
- Create: `Tests/CRPC.Tests/CRpcClientHeartbeatHandlerTests.cs`
- Modify: `Tests/CRPC.Tests/CRpcClientPipelineFactoryTests.cs`

- [ ] **Step 1: Write failing handler test**

Create `Tests/CRPC.Tests/CRpcClientHeartbeatHandlerTests.cs`:

```csharp
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public sealed class CRpcClientHeartbeatHandlerTests
{
    [Fact]
    public void WriterIdleEventWritesHeartbeatFrame()
    {
        var channel = new EmbeddedChannel(
            new CRpcMessageEncoder(),
            new CRpcClientHeartbeatHandler());
        channel.Pipeline.FireUserEventTriggered(IdleStateEvent.DefaultWriterIdleState);

        var outbound = channel.ReadOutbound<IByteBuffer>();
        Assert.NotNull(outbound);
        try
        {
            Assert.Equal(CRpcMessage.Magic, outbound.ReadInt());
            var payloadLen = outbound.ReadInt();
            Assert.Equal(CRpcMessageHeader.FixedLength, payloadLen);
            var header = CRpcMessageHeader.ReadFrom(outbound);
            Assert.Equal(CRpcMessageType.Heartbeat, header.MessageType);
        }
        finally
        {
            outbound.Release();
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcClientHeartbeatHandlerTests" -v q
```

Expected: FAIL — type not found.

- [ ] **Step 3: Implement handler and update pipeline factory**

Create `CRpc/Rpc/CRpc/Client/CRpcClientHeartbeatHandler.cs`:

```csharp
using CRpc.Rpc.CRpc;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Client;

internal sealed class CRpcClientHeartbeatHandler : ChannelHandlerAdapter
{
    public override void UserEventTriggered(IChannelHandlerContext context, object evt)
    {
        if (evt is IdleStateEvent idleStateEvent
            && idleStateEvent.State == IdleState.WriterIdle)
        {
            ChannelWriteUtil.WriteAndFlushFireAndForget(context, CRpcMessage.CreateHeartbeat());
            return;
        }

        base.UserEventTriggered(context, evt);
    }
}
```

Replace `CRpcClientPipelineFactory.Configure` body:

```csharp
public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
{
    ArgumentNullException.ThrowIfNull(pipeline);
    ArgumentNullException.ThrowIfNull(host);

    options.Validate();

    if (options.HeartbeatEnabled)
    {
        pipeline.AddLast(
            "idle",
            new IdleStateHandler(0, options.HeartbeatIntervalSeconds, 0));
        pipeline.AddLast("heartbeat", new CRpcClientHeartbeatHandler());
    }

    pipeline.AddLast("decoder", new CRpcMessageDecoder(options.MaxFrameLength));
    pipeline.AddLast("encoder", new CRpcMessageEncoder());
    pipeline.AddLast("handler", new LoopInboundHandler(host));
}
```

- [ ] **Step 4: Update pipeline factory tests**

Replace `HeartbeatIdleSeconds = 17` with `HeartbeatIntervalSeconds = 17` and add:

```csharp
[Fact]
public void ConfigureAddsHeartbeatHandlerWhenEnabled()
{
    var options = new CRpcClientOptions { HeartbeatIntervalSeconds = 17 };
    var loop = new CRpcLoop();
    var host = new TcpChannelHost(loop, new CRpcClientPipelineFactory(options));
    var channel = new EmbeddedChannel();

    host.PipelineFactory.Configure(channel.Pipeline, host);

    Assert.NotNull(channel.Pipeline.Get<CRpcClientHeartbeatHandler>());
}

[Fact]
public void ConfigureOmitsIdleHandlersWhenDisabled()
{
    var options = new CRpcClientOptions { HeartbeatEnabled = false };
    var loop = new CRpcLoop();
    var host = new TcpChannelHost(loop, new CRpcClientPipelineFactory(options));
    var channel = new EmbeddedChannel();

    host.PipelineFactory.Configure(channel.Pipeline, host);

    Assert.Null(channel.Pipeline.Get<IdleStateHandler>());
    Assert.Null(channel.Pipeline.Get<CRpcClientHeartbeatHandler>());
}
```

- [ ] **Step 5: Run client pipeline + handler tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcClientHeartbeatHandlerTests|FullyQualifiedName~CRpcClientPipelineFactoryTests" -v q
```

Expected: PASS

---

## Task 4: Server read-idle handler and pipeline factory

**Files:**
- Create: `CRpc/Rpc/CRpc/Server/CRpcServerReadIdleHandler.cs`
- Create: `CRpc/Rpc/CRpc/Server/CRpcServerPipelineFactory.cs`
- Create: `Tests/CRPC.Tests/CRpcServerReadIdleHandlerTests.cs`
- Create: `Tests/CRPC.Tests/CRpcServerPipelineFactoryTests.cs`

- [ ] **Step 1: Write failing server handler test**

Create `Tests/CRPC.Tests/CRpcServerReadIdleHandlerTests.cs`:

```csharp
using CRpc.Rpc.CRpc.Server;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public sealed class CRpcServerReadIdleHandlerTests
{
    [Fact]
    public void ReaderIdleEventClosesChannel()
    {
        var channel = new EmbeddedChannel(new CRpcServerReadIdleHandler());
        channel.Pipeline.FireUserEventTriggered(IdleStateEvent.DefaultReaderIdleState);

        Assert.False(channel.Active);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcServerReadIdleHandlerTests" -v q
```

Expected: FAIL

- [ ] **Step 3: Implement server idle handler and pipeline factory**

Create `CRpc/Rpc/CRpc/Server/CRpcServerReadIdleHandler.cs`:

```csharp
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Server;

internal sealed class CRpcServerReadIdleHandler : ChannelHandlerAdapter
{
    public override void UserEventTriggered(IChannelHandlerContext context, object evt)
    {
        if (evt is IdleStateEvent idleStateEvent
            && idleStateEvent.State == IdleState.ReaderIdle)
        {
            _ = context.CloseAsync();
            return;
        }

        base.UserEventTriggered(context, evt);
    }
}
```

Create `CRpc/Rpc/CRpc/Server/CRpcServerPipelineFactory.cs`:

```csharp
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcServerPipelineFactory
{
    private readonly CRpcServerOptions options;

    public CRpcServerPipelineFactory(CRpcServerOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Configure(IChannelPipeline pipeline, IChannelHandler appHandler)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(appHandler);

        options.Validate();

        if (options.HeartbeatEnabled)
        {
            pipeline.AddLast(
                "idle",
                new IdleStateHandler(options.ReadIdleSeconds, 0, 0));
            pipeline.AddLast("idle-handler", new CRpcServerReadIdleHandler());
        }

        pipeline.AddLast("decoder", new CRpcMessageDecoder(options.MaxFrameLength));
        pipeline.AddLast("encoder", new CRpcMessageEncoder());
        pipeline.AddLast("handler", appHandler);
    }
}
```

- [ ] **Step 4: Write pipeline factory tests**

Create `Tests/CRPC.Tests/CRpcServerPipelineFactoryTests.cs`:

```csharp
using CRpc.Rpc.CRpc.Server;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public sealed class CRpcServerPipelineFactoryTests
{
    [Fact]
    public void ConfigureAddsReadIdleHandlersWhenEnabled()
    {
        var options = new CRpcServerOptions { ReadIdleSeconds = 45 };
        var factory = new CRpcServerPipelineFactory(options);
        var channel = new EmbeddedChannel();

        factory.Configure(channel.Pipeline, new ChannelHandlerAdapter());

        Assert.NotNull(channel.Pipeline.Get<IdleStateHandler>());
        Assert.NotNull(channel.Pipeline.Get<CRpcServerReadIdleHandler>());
        Assert.NotNull(channel.Pipeline.Get<CRpcMessageDecoder>());
        Assert.NotNull(channel.Pipeline.Get<CRpcMessageEncoder>());
    }

    [Fact]
    public void ConfigureOmitsIdleHandlersWhenDisabled()
    {
        var options = new CRpcServerOptions { HeartbeatEnabled = false };
        var factory = new CRpcServerPipelineFactory(options);
        var channel = new EmbeddedChannel();

        factory.Configure(channel.Pipeline, new ChannelHandlerAdapter());

        Assert.Null(channel.Pipeline.Get<IdleStateHandler>());
        Assert.Null(channel.Pipeline.Get<CRpcServerReadIdleHandler>());
    }
}
```

- [ ] **Step 5: Run server handler + pipeline tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcServerReadIdleHandlerTests|FullyQualifiedName~CRpcServerPipelineFactoryTests" -v q
```

Expected: PASS

---

## Task 5: Wire server pipeline into CRpcServer and UnifiedServer

**Files:**
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServer.cs`
- Modify: `Example/HelloWorld/Server/Http/UnifiedServer.cs`

- [ ] **Step 1: Replace inline CRpcServer ChildHandler wiring**

In `CRpcServer.cs` `StartInternalAsync`, replace pipeline block:

```csharp
.ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
{
    var handler = options.HandlerFactory?.Invoke(this) ?? new CRpcServerHandler(this);
    new CRpcServerPipelineFactory(startOptions).Configure(channel.Pipeline, handler);
}));
```

Use `startOptions` (bound copy) so per-start options are applied.

- [ ] **Step 2: Update UnifiedServer CRpc branch**

In `UnifiedServer.cs`, replace CRpc branch inside `PortUnificationHandler`:

```csharp
ctx =>
{
    new CRpcServerPipelineFactory(crpcServer.Options).Configure(
        ctx.Channel.Pipeline,
        new CRpcServerHandler(crpcServer));
},
```

Remove direct `CRpcMessageDecoder` / `CRpcMessageEncoder` / handler `AddLast` calls.

- [ ] **Step 3: Build solution**

Run:

```bash
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj
dotnet build Example/GateWay/GateWayServer/GateWayServer.csproj
```

Expected: SUCCESS

---

## Task 6: Handler short-circuit and CRpcClient ConnectionLost

**Files:**
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs`
- Modify: `Example/GateWay/GateWay.Core/GateWayServerHandler.cs`
- Modify: `CRpc/Rpc/CRpc/Client/CRpcClient.cs`
- Modify: `Tests/CRPC.Tests/CRpcServerHandlerTests.cs`
- Modify: `Tests/CRPC.Tests/CRpcClientTests.cs`

- [ ] **Step 1: Write failing server handler test**

Add to `CRpcServerHandlerTests.cs`:

```csharp
[Fact]
public void HeartbeatIsIgnoredWithoutDispatchingToService()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var service = new ContextRecordingService(NextServiceId());
    var server = new CRpcServer(loop);
    RegisterOnLoop(loop, service);
    var channel = CreateHandlerChannel(server);
    channel.Pipeline.FireChannelActive();
    loop.Tick();

    channel.WriteInbound(CRpcMessage.CreateHeartbeat());
    loop.Tick();

    Assert.Equal(0, service.CallCount);
    Assert.Empty(channel.OutboundMessages);
}
```

- [ ] **Step 2: Write failing ConnectionLost test**

Add to `CRpcClientTests.cs`:

```csharp
[Fact]
public void ChannelInactiveRaisesConnectionLostOnOwnerLoop()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var client = new CRpcClient(loop);
    var channel = new EmbeddedChannel();
    SetClientHostChannel(client, channel);
    var lost = false;
    client.ConnectionLost += () => lost = true;

    channel.Pipeline.FireChannelInactive();
    loop.Tick();

    Assert.True(lost);
}

[Fact]
public void InboundHeartbeatIsIgnored()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var client = new CRpcClient(loop);
    var channel = new EmbeddedChannel(new CRpcMessageDecoder(65536));
    SetClientHostChannel(client, channel);

    channel.WriteInbound(CRpcMessage.CreateHeartbeat());
    loop.Tick();

    Assert.Empty(client /* no pending calls — use existing helper or assert no throw */);
}
```

For `InboundHeartbeatIsIgnored`, assert no exception and no pending state change — simplest: invoke host callback path by writing decoded message through `LoopInboundHandler` if wired; or call internal receive path. Prefer wiring full mini-pipeline:

```csharp
var host = /* get host via reflection from client */;
host.InboundMessageReceived = msg => { /* no-op counter */ };
```

Use pattern from existing `CRpcClientTests` — fire inbound via `PostInboundMessage` on host after decode, or write through EmbeddedChannel with `LoopInboundHandler(host)`.

Minimal version:

```csharp
[Fact]
public void InboundHeartbeatDoesNotCompletePendingCall()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var client = new CRpcClient(loop);
    SetClientHostChannel(client, new EmbeddedChannel());

    // Simulate host delivering heartbeat — use reflection to call OnHostInboundMessage
    // OR add test hook. Preferred: invoke public path via host.InboundMessageReceived wiring is internal.
    // Use same reflection helper as other CRpcClientTests for OnHostInboundMessage:
    InvokeHostInboundMessage(client, CRpcMessage.CreateHeartbeat());
    // Assert pending table empty and no exception
}
```

Check `CRpcClientTests` for `InvokeHostInboundMessage` or similar — if missing, plan step uses reflection:

```csharp
private static void InvokeHostInboundMessage(CRpcClient client, CRpcMessage message)
{
    var method = typeof(CRpcClient).GetMethod("OnHostInboundMessage",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.NotNull(method);
    method!.Invoke(client, new object[] { message });
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcServerHandlerTests.Heartbeat|FullyQualifiedName~CRpcClientTests.ChannelInactiveRaisesConnectionLost|FullyQualifiedName~CRpcClientTests.InboundHeartbeat" -v q
```

Expected: FAIL

- [ ] **Step 4: Implement handler and client changes**

In `CRpcServerHandler.ChannelRead`, before Request handling:

```csharp
if (message.MessageType == CRpcMessageType.Heartbeat)
{
    return;
}
```

Same at start of `GateWayServerHandler.ChannelRead` (before `server.Loop.Post`).

In `CRpcClient.cs`:

```csharp
public event Action? ConnectionLost;

private void OnHostChannelInactive()
{
    FailPendingCalls(new ConnectionClosedException("CRpcClient channel became inactive."));
    ConnectionLost?.Invoke();
}
```

In `CompleteReceiveResponse`:

```csharp
case CRpcMessageType.Heartbeat:
    return;
```

- [ ] **Step 5: Run tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcServerHandlerTests|FullyQualifiedName~CRpcClientTests" -v q
```

Expected: PASS (or fix any regressions in client tests)

---

## Task 7: Gateway backend ConnectionLost

**Files:**
- Modify: `Example/GateWay/GateWay.Core/GateWaySessionTable.cs`
- Modify: `Tests/CRPC.Tests/GateWay/GateWaySessionTableTests.cs`

- [ ] **Step 1: Write failing Gateway test**

Add to `GateWaySessionTableTests.cs`:

```csharp
[Fact]
public void BackendConnectionLostRemovesLinkAndMarksEndpointUnhealthy()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var registry = GateWayTestHelpers.CreateRegistry(GreeterServiceId, ("127.0.0.1", 7999));
    var factory = new CapturingBackendClientFactory(loop);
    var table = GateWayTestHelpers.CreateSessionTable(factory, new SuccessBackendConnector(), registry);
    var inbound = RegisterInboundConnection(loop);

    var link = CRpcLoopRunner.RunUntilComplete(
        loop,
        async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));
    Assert.NotNull(link);

    loop.Post(() => factory.LastClient!.ConnectionLost?.Invoke());
    loop.Tick();

    Assert.Null(table.TryGet(inbound.ConnectionId));
    Assert.True(registry.TryGetPool(GreeterServiceId, out var pool));
    Assert.False(pool!.Endpoints[0].IsHealthy);
}

private sealed class CapturingBackendClientFactory : global::GateWay.IBackendClientFactory
{
    private readonly CRpcLoop loop;
    public CapturingBackendClientFactory(CRpcLoop loop) => this.loop = loop;
    public CRpc.Rpc.CRpc.Client.CRpcClient? LastClient { get; private set; }

    public CRpc.Rpc.CRpc.Client.CRpcClient Create(CRpcLoop loop)
    {
        LastClient = new CRpc.Rpc.CRpc.Client.CRpcClient(this.loop);
        return LastClient;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~BackendConnectionLostRemovesLink" -v q
```

Expected: FAIL — link not removed on `ConnectionLost`.

- [ ] **Step 3: Implement GateWaySessionTable subscription**

In `GetOrCreateAsync`, after creating `link`:

```csharp
var inboundConnectionId = inbound.ConnectionId;
var endpoint = endpoint; // captured pick
client.ConnectionLost += () =>
{
    loop.Post(() => HandleBackendConnectionLost(inboundConnectionId, serviceId, endpoint));
};
links[inbound.ConnectionId] = link;
```

Add private method:

```csharp
private void HandleBackendConnectionLost(long inboundConnectionId, ushort serviceId, BackendEndpoint endpoint)
{
    links.Remove(inboundConnectionId);
    if (poolRegistry.TryGetPool(serviceId, out var pool))
    {
        pool.MarkUnhealthy(endpoint);
    }
}
```

Ensure handler runs on owner loop (`loop.Post` in subscription). Do **not** dispose backend client inside `ConnectionLost` if already inactive — removing from table is enough; disposal happens on inbound `RemoveAsync` or explicit dispose. If link removed while client already dead, no double-close issue.

- [ ] **Step 4: Run Gateway session tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~GateWaySessionTableTests" -v q
```

Expected: PASS

---

## Task 8: Documentation and full regression

**Files:**
- Modify: `Doc/protocol.md`
- Modify: `docs/superpowers/specs/2026-06-21-crpc-heartbeat-v1-design.md`

- [ ] **Step 1: Update protocol doc**

In `Doc/protocol.md` header table, change messageType row:

```markdown
| 1 | `messageType` — 0 Request, 1 Response, 2 Push, 3 Heartbeat |
```

Add to Message conventions table:

```markdown
| Heartbeat | 0 | 0 |
```

Add short section:

```markdown
## Heartbeat (v1)

- Client sends `Heartbeat` on a fixed writer-idle interval (default 15s).
- Server does not reply. Any inbound frame (RPC or Heartbeat) resets the server read-idle timer (default 45s).
- Server closes the connection when read idle expires.
```

- [ ] **Step 2: Link plan in spec**

In `docs/superpowers/specs/2026-06-21-crpc-heartbeat-v1-design.md`, replace plan placeholder:

```markdown
**Plan:** `docs/superpowers/plans/2026-06-21-crpc-heartbeat-v1.md`
```

Set spec **Status:** `Approved` if user has approved design.

- [ ] **Step 3: Full test suite**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj -v q
```

Expected: all tests PASS

- [ ] **Step 4: Manual smoke (optional)**

Terminal 1:

```bash
dotnet run --project Example/HelloWorld/Server -- --port 7999
```

Terminal 2 — connect a reference client, idle 60s without RPC; connection should stay up (heartbeats). Stop server; client should see disconnect on next call or `ConnectionLost`.

---

## Spec Coverage Checklist

| Spec requirement | Task |
|------------------|------|
| `Heartbeat` message type + `CreateHeartbeat()` | Task 1 |
| Server no ack | Tasks 4–6 (no ack code anywhere) |
| Client writer idle 15s default | Tasks 2–3 |
| Server read idle 45s default | Tasks 2, 4–5 |
| `HeartbeatEnabled` toggle | Tasks 2–4 |
| Options validation | Task 2 |
| `CRpcServerPipelineFactory` | Tasks 4–5 |
| `UnifiedServer` uses factory | Task 5 |
| Handler Heartbeat short-circuit | Task 6 |
| `CRpcClient.ConnectionLost` | Task 6 |
| Ignore inbound Heartbeat on client | Task 6 |
| Gateway backend `ConnectionLost` | Task 7 |
| `Doc/protocol.md` | Task 8 |
| Full regression | Task 8 |

## Type Consistency Notes

- Use `HeartbeatIntervalSeconds` everywhere; grep and remove all `HeartbeatIdleSeconds` references (including `Tests/CRPC.Tests/CRpcClientPipelineFactoryTests.cs`).
- `CRpcServerPipelineFactory` is `public` so `Example/HelloWorld` can reference it from the same `CRpc` assembly/project reference.
- Gateway example project already references `CRpcClient`; `ConnectionLost` is public on `CRpcClient`.
