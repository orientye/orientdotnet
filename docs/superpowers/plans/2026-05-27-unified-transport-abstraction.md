# Unified Transport Abstraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a shared DotNetty TCP channel foundation under `CRPC/Transport`, then migrate the LordUnion live game-server transport to it without changing `CRpcClient` in the first delivery.

**Architecture:** Keep transport, protocol codec, and business semantics separate. `CRPC/Transport` owns connection lifecycle, writes, channel events, and `CRpcLoop` ingress. LordUnion keeps its `0x14801` frame protocol in `Tests/LordUnion.IntegrationTests/Protocol`, and `GameServerDotNettyTransport` adapts decoded frames back into the existing `IGameServerTransport` and `AccountSession` flow. `CRpcClient`, `CRpcMessageEncoder/Decoder`, `serviceId`, and `methodId` remain unchanged in this plan.

**Tech Stack:** C# / .NET 8, DotNetty, `CRpcLoop`, `CRpcTask`, xUnit, LordUnion protobuf-generated messages.

**Spec reference:** `docs/superpowers/specs/2026-05-27-unified-transport-abstraction-design.md`

**Design Decisions:**
- Scope option A: do not modify `CRpcClient` in this delivery.
- Put shared channel infrastructure in `CRPC/Transport`.
- Put GameServer frame codec in `Tests/LordUnion.IntegrationTests/Protocol`.
- Add `GameServerDotNettyTransport` and make it the default live transport.
- Keep `GameServerTcpTransport` temporarily as migration reference, but do not add a long-term user-facing transport selector.
- Do not auto-commit changes unless the user explicitly asks for a commit.

---

## File Structure

| File / Directory | Responsibility |
| --- | --- |
| `CRPC/Transport/TcpChannelHostOptions.cs` | DotNetty host options shared by protocol-specific clients |
| `CRPC/Transport/IChannelPipelineFactory.cs` | Protocol-specific pipeline configuration boundary |
| `CRPC/Transport/LoopInboundHandler.cs` | DotNetty inbound handler that posts channel events to the owning `CRpcLoop` |
| `CRPC/Transport/TcpChannelHost.cs` | Shared DotNetty TCP lifecycle: connect, write, close, shutdown |
| `Tests/CRPC.Tests/Transport/TcpChannelHostOptionsTests.cs` | Option default and validation tests |
| `Tests/CRPC.Tests/Transport/LoopInboundHandlerTests.cs` | Verify inbound events run on the owning loop |
| `Tests/CRPC.Tests/Transport/TcpChannelHostTests.cs` | Verify connect/write/close behavior with a local DotNetty test server or embedded channel seam |
| `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrame.cs` | LordUnion game-server frame envelope |
| `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrameDecoder.cs` | DotNetty decoder for 8-byte game-server frame headers and body payloads |
| `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrameEncoder.cs` | DotNetty encoder for game-server outbound frames |
| `Tests/LordUnion.IntegrationTests/Protocol/GameServerPipelineFactory.cs` | LordUnion-specific pipeline assembly |
| `Tests/LordUnion.IntegrationTests/Sessions/GameServerDotNettyTransport.cs` | `IGameServerTransport` implementation backed by `TcpChannelHost` |
| `Tests/LordUnion.IntegrationTests/Properties/AssemblyInfo.cs` | Test assembly friend visibility for focused transport tests |
| `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs` | Default live transport creation switches from TcpClient to DotNetty |
| `Tests/CRPC.Tests/LordUnion/GameServerFrameCodecTests.cs` | Codec parity, sticky-packet, and partial-frame tests |
| `Tests/CRPC.Tests/LordUnion/GameServerDotNettyTransportTests.cs` | Transport-to-session delivery tests |

---

## Task 1: Baseline Current LordUnion Behavior

**Files:**
- Read/reference: `Tests/LordUnion.IntegrationTests/Sessions/GameServerTcpTransport.cs`
- Read/reference: `Tests/LordUnion.IntegrationTests/Sessions/IGameServerTransport.cs`
- Read/reference: `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`
- Read/reference: `Tests/LordUnion.IntegrationTests/lordunion-test-output/scenario-report-20260527T105628Z.json`

- [ ] **Step 1: Run the existing LordUnion unit tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~LordUnion"
```

Expected: all existing LordUnion tests pass before any transport changes.

- [ ] **Step 2: Record the latest live success report**

Open the latest known successful report and record these fields in implementation notes:

```text
scenarioName
success
matchId
tableId
winSeat
accountTimings[*].loginDurationMs
accountTimings[*].enterMatchDurationMs
accountTimings[*].gameDurationMs
```

Use `Tests/LordUnion.IntegrationTests/lordunion-test-output/scenario-report-20260527T105628Z.json` as the current baseline if it remains the latest successful run.

- [ ] **Step 3: Confirm no CRpcClient work belongs in this phase**

Review `CRPC/Rpc/CRpc/Client/CRpcClient.cs` and confirm the implementation tasks below do not change:

```text
CRpcClient.CallAsync
CRpcClient.ConnectAsync
CRpcClient.CloseAsync
CRpcClient.ShutdownIoAsync
CRpcClient.OnReceiveResponse
CRpcClient.OnChannelInactive
CRpcClient.OnChannelException
```

If a later implementation step appears to require editing those methods, stop and revise the plan instead of expanding scope.

---

## Task 2: Add Shared TcpChannelHost Options

**Files:**
- Create: `CRPC/Transport/TcpChannelHostOptions.cs`
- Create: `Tests/CRPC.Tests/Transport/TcpChannelHostOptionsTests.cs`

- [ ] **Step 1: Write option default tests**

Create `Tests/CRPC.Tests/Transport/TcpChannelHostOptionsTests.cs`:

```csharp
using CRpc.Transport;

namespace CRPC.Tests.Transport;

public sealed class TcpChannelHostOptionsTests
{
    [Fact]
    public void DefaultsAreSuitableForExistingClientTransport()
    {
        var options = new TcpChannelHostOptions();

        Assert.Equal(TcpChannelHostOptions.DefaultIoThreadCount, options.IoThreadCount);
        Assert.Equal(TcpChannelHostOptions.DefaultConnectTimeoutSeconds, options.ConnectTimeoutSeconds);
        Assert.True(options.TcpNoDelay);
        Assert.Equal("tcp-channel", options.LoggingName);
    }

    [Fact]
    public void ValidateRejectsInvalidThreadCount()
    {
        var options = new TcpChannelHostOptions { IoThreadCount = 0 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

        Assert.Equal("IoThreadCount", exception.ParamName);
    }

    [Fact]
    public void ValidateRejectsInvalidConnectTimeout()
    {
        var options = new TcpChannelHostOptions { ConnectTimeoutSeconds = 0 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

        Assert.Equal("ConnectTimeoutSeconds", exception.ParamName);
    }
}
```

- [ ] **Step 2: Run the option tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter TcpChannelHostOptionsTests
```

Expected: fail because `CRpc.Transport.TcpChannelHostOptions` does not exist.

- [ ] **Step 3: Implement `TcpChannelHostOptions`**

Create `CRPC/Transport/TcpChannelHostOptions.cs`:

```csharp
namespace CRpc.Transport;

public sealed class TcpChannelHostOptions
{
    public const int DefaultIoThreadCount = 1;
    public const int DefaultConnectTimeoutSeconds = 10;

    public int IoThreadCount { get; init; } = DefaultIoThreadCount;

    public int ConnectTimeoutSeconds { get; init; } = DefaultConnectTimeoutSeconds;

    public bool TcpNoDelay { get; init; } = true;

    public string LoggingName { get; init; } = "tcp-channel";

    public void Validate()
    {
        if (IoThreadCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(IoThreadCount),
                IoThreadCount,
                "TcpChannelHostOptions.IoThreadCount must be positive.");
        }

        if (ConnectTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConnectTimeoutSeconds),
                ConnectTimeoutSeconds,
                "TcpChannelHostOptions.ConnectTimeoutSeconds must be positive.");
        }

        if (string.IsNullOrWhiteSpace(LoggingName))
        {
            throw new ArgumentException(
                "TcpChannelHostOptions.LoggingName must not be empty.",
                nameof(LoggingName));
        }
    }
}
```

- [ ] **Step 4: Run the option tests and verify pass**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter TcpChannelHostOptionsTests
```

Expected: pass.

---

## Task 3: Add Pipeline Factory Boundary

**Files:**
- Create: `CRPC/Transport/IChannelPipelineFactory.cs`
- Create: `Tests/CRPC.Tests/Transport/IChannelPipelineFactoryTests.cs`

- [ ] **Step 1: Write a compile-boundary test**

Create `Tests/CRPC.Tests/Transport/IChannelPipelineFactoryTests.cs`:

```csharp
using CRpc.Transport;
using DotNetty.Transport.Channels;

namespace CRPC.Tests.Transport;

public sealed class IChannelPipelineFactoryTests
{
    [Fact]
    public void FactoryCanBeImplementedByProtocolSpecificCode()
    {
        IChannelPipelineFactory factory = new RecordingPipelineFactory();

        Assert.NotNull(factory);
    }

    private sealed class RecordingPipelineFactory : IChannelPipelineFactory
    {
        public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            ArgumentNullException.ThrowIfNull(host);
        }
    }
}
```

- [ ] **Step 2: Run the test and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter IChannelPipelineFactoryTests
```

Expected: fail because `IChannelPipelineFactory` and `TcpChannelHost` do not exist.

- [ ] **Step 3: Add a temporary `TcpChannelHost` shell and the factory interface**

Create `CRPC/Transport/IChannelPipelineFactory.cs`:

```csharp
using DotNetty.Transport.Channels;

namespace CRpc.Transport;

public interface IChannelPipelineFactory
{
    void Configure(IChannelPipeline pipeline, TcpChannelHost host);
}
```

Create the first shell of `CRPC/Transport/TcpChannelHost.cs`:

```csharp
using CRpc.Async;

namespace CRpc.Transport;

public sealed class TcpChannelHost : IAsyncDisposable
{
    private readonly CRpcLoop ownerLoop;
    private readonly IChannelPipelineFactory pipelineFactory;
    private readonly TcpChannelHostOptions options;

    public TcpChannelHost(
        CRpcLoop ownerLoop,
        IChannelPipelineFactory pipelineFactory,
        TcpChannelHostOptions? options = null)
    {
        this.ownerLoop = ownerLoop ?? throw new ArgumentNullException(nameof(ownerLoop));
        this.pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
        this.options = options ?? new TcpChannelHostOptions();
        this.options.Validate();
    }

    public CRpcLoop OwnerLoop => ownerLoop;

    public IChannelPipelineFactory PipelineFactory => pipelineFactory;

    public TcpChannelHostOptions Options => options;

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 4: Run pipeline factory tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "IChannelPipelineFactoryTests|TcpChannelHostOptionsTests"
```

Expected: pass.

---

## Task 4: Add Loop Ingress Handler

**Files:**
- Create: `CRPC/Transport/LoopInboundHandler.cs`
- Modify: `CRPC/Transport/TcpChannelHost.cs`
- Create: `Tests/CRPC.Tests/Transport/LoopInboundHandlerTests.cs`

- [ ] **Step 1: Write inbound event tests**

Create `Tests/CRPC.Tests/Transport/LoopInboundHandlerTests.cs`:

```csharp
using CRpc.Async;
using CRpc.Transport;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests.Transport;

public sealed class LoopInboundHandlerTests
{
    [Fact]
    public void ChannelReadPostsInboundMessageToOwnerLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        object? received = null;
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
        {
            InboundMessageReceived = message => received = message
        };
        var channel = new EmbeddedChannel(new LoopInboundHandler(host));
        var payload = new object();

        channel.WriteInbound(payload);
        loop.Tick();

        Assert.Same(payload, received);
    }

    [Fact]
    public void ExceptionCaughtPostsExceptionToOwnerLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        Exception? received = null;
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
        {
            ChannelExceptionCaught = exception => received = exception
        };
        var channel = new EmbeddedChannel(new LoopInboundHandler(host));
        var expected = new InvalidOperationException("boom");

        channel.Pipeline.FireExceptionCaught(expected);
        loop.Tick();

        Assert.Same(expected, received);
    }

    private sealed class EmptyPipelineFactory : IChannelPipelineFactory
    {
        public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
        {
        }
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LoopInboundHandlerTests
```

Expected: fail because `LoopInboundHandler` and host callbacks do not exist.

- [ ] **Step 3: Add host callbacks**

Modify `CRPC/Transport/TcpChannelHost.cs` to add callback properties and internal event methods:

```csharp
public Action<object>? InboundMessageReceived { get; init; }

public Action? ChannelBecameInactive { get; init; }

public Action<Exception>? ChannelExceptionCaught { get; init; }

internal void PostInboundMessage(object message)
{
    ownerLoop.Post(() => InboundMessageReceived?.Invoke(message));
}

internal void PostChannelInactive()
{
    ownerLoop.Post(() => ChannelBecameInactive?.Invoke());
}

internal void PostChannelException(Exception exception)
{
    ArgumentNullException.ThrowIfNull(exception);
    ownerLoop.Post(() => ChannelExceptionCaught?.Invoke(exception));
}
```

Keep the constructor and properties from Task 3.

- [ ] **Step 4: Implement `LoopInboundHandler`**

Create `CRPC/Transport/LoopInboundHandler.cs`:

```csharp
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;

namespace CRpc.Transport;

public sealed class LoopInboundHandler : ChannelHandlerAdapter
{
    private readonly TcpChannelHost host;

    public LoopInboundHandler(TcpChannelHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public override void ChannelRead(IChannelHandlerContext context, object message)
    {
        host.PostInboundMessage(message);
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        host.PostChannelInactive();
        base.ChannelInactive(context);
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        host.PostChannelException(exception);
        _ = context.CloseAsync();
    }
}
```

Do not call `ReferenceCountUtil.Release(message)` in `ChannelRead`. The decoded message is handed to the owner loop and must remain valid for the consumer. If a later decoder emits reference-counted buffers, that protocol handler must convert them to owned objects before they reach `LoopInboundHandler`.

- [ ] **Step 5: Run ingress tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LoopInboundHandlerTests
```

Expected: pass.

---

## Task 5: Implement TcpChannelHost Lifecycle

**Files:**
- Modify: `CRPC/Transport/TcpChannelHost.cs`
- Create: `Tests/CRPC.Tests/Transport/TcpChannelHostTests.cs`

- [ ] **Step 1: Write host lifecycle tests**

Create `Tests/CRPC.Tests/Transport/TcpChannelHostTests.cs`:

```csharp
using CRpc.Async;
using CRpc.Transport;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;

namespace CRPC.Tests.Transport;

public sealed class TcpChannelHostTests
{
    [Fact]
    public void ConnectAsyncThrowsWhenNotOnOwnerLoop()
    {
        var loop = new CRpcLoop();
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            host.ConnectAsync("127.0.0.1", 1));

        Assert.Contains("owner CRpcLoop", exception.Message);
    }

    [Fact]
    public void WriteAndFlushAsyncThrowsWhenNotConnected()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            host.WriteAndFlushAsync(Unpooled.Empty));

        Assert.Contains("not connected", exception.Message);
    }

    [Fact]
    public void CloseAsyncCompletesWhenNeverConnected()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory());

        var closeTask = host.CloseAsync();

        Assert.True(closeTask.GetAwaiter().IsCompleted);
    }

    private sealed class EmptyPipelineFactory : IChannelPipelineFactory
    {
        public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
        {
            pipeline.AddLast(new LoopInboundHandler(host));
        }
    }
}
```

- [ ] **Step 2: Run host tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter TcpChannelHostTests
```

Expected: fail because lifecycle methods are not implemented.

- [ ] **Step 3: Implement lifecycle fields and owner-loop check**

Modify `CRPC/Transport/TcpChannelHost.cs` to include DotNetty fields:

```csharp
using CRpc.Async;
using DotNetty.Handlers.Logging;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace CRpc.Transport;

public sealed class TcpChannelHost : IAsyncDisposable
{
    private readonly CRpcLoop ownerLoop;
    private readonly IChannelPipelineFactory pipelineFactory;
    private readonly TcpChannelHostOptions options;
    private readonly IEventLoopGroup group;
    private readonly Bootstrap bootstrap;
    private IChannel? channel;

    public TcpChannelHost(
        CRpcLoop ownerLoop,
        IChannelPipelineFactory pipelineFactory,
        TcpChannelHostOptions? options = null)
    {
        this.ownerLoop = ownerLoop ?? throw new ArgumentNullException(nameof(ownerLoop));
        this.pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
        this.options = options ?? new TcpChannelHostOptions();
        this.options.Validate();
        group = new MultithreadEventLoopGroup(this.options.IoThreadCount);
        bootstrap = new Bootstrap()
            .Channel<TcpSocketChannel>()
            .Option(ChannelOption.TcpNodelay, this.options.TcpNoDelay)
            .Option(ChannelOption.ConnectTimeout, TimeSpan.FromSeconds(this.options.ConnectTimeoutSeconds))
            .Group(group)
            .Handler(new ActionChannelInitializer<ISocketChannel>(socket =>
            {
                var pipeline = socket.Pipeline;
                pipeline.AddLast(new LoggingHandler(this.options.LoggingName));
                this.pipelineFactory.Configure(pipeline, this);
            }));
    }

    public CRpcLoop OwnerLoop => ownerLoop;

    public IChannelPipelineFactory PipelineFactory => pipelineFactory;

    public TcpChannelHostOptions Options => options;

    public Action<object>? InboundMessageReceived { get; init; }

    public Action? ChannelBecameInactive { get; init; }

    public Action<Exception>? ChannelExceptionCaught { get; init; }

    public bool IsConnected => channel is not null && channel.Active;

    private void EnsureOwnerLoopThread()
    {
        if (!ownerLoop.IsInLoopThread)
        {
            throw new InvalidOperationException(
                "TcpChannelHost operations must run on the owner CRpcLoop thread.");
        }
    }
}
```

Keep the `PostInboundMessage`, `PostChannelInactive`, and `PostChannelException` methods added in Task 4.

- [ ] **Step 4: Implement connect, write, close, and shutdown**

Add these methods to `TcpChannelHost`:

```csharp
public CRpcTask<IChannel> ConnectAsync(string host, int port)
{
    EnsureOwnerLoopThread();
    ArgumentException.ThrowIfNullOrWhiteSpace(host);
    if (port <= 0 || port > 65535)
    {
        throw new ArgumentOutOfRangeException(nameof(port), port, "TCP port must be in range 1..65535.");
    }

    if (channel is not null)
    {
        throw new InvalidOperationException("TcpChannelHost is already connected.");
    }

    return ConnectCoreAsync(host, port);
}

private async CRpcTask<IChannel> ConnectCoreAsync(string host, int port)
{
    var connected = await CRpcTask.FromTask(bootstrap.ConnectAsync(host, port), ownerLoop);
    channel = connected;
    return connected;
}

public CRpcTask WriteAndFlushAsync(object message)
{
    EnsureOwnerLoopThread();
    ArgumentNullException.ThrowIfNull(message);

    var currentChannel = channel
        ?? throw new InvalidOperationException("TcpChannelHost is not connected.");

    return CRpcTask.FromTask(currentChannel.WriteAndFlushAsync(message), ownerLoop);
}

public CRpcTask CloseAsync()
{
    EnsureOwnerLoopThread();

    var currentChannel = channel;
    channel = null;

    if (currentChannel is null)
    {
        return CRpcTask.CompletedTask(ownerLoop);
    }

    return CRpcTask.FromTask(currentChannel.CloseAsync(), ownerLoop);
}

public CRpcTask ShutdownIoAsync()
{
    EnsureOwnerLoopThread();
    return CRpcTask.FromTask(group.ShutdownGracefullyAsync(), ownerLoop);
}

public ValueTask DisposeAsync()
{
    EnsureOwnerLoopThread();

    var closeAwaiter = CloseAsync().GetAwaiter();
    if (!closeAwaiter.IsCompleted)
    {
        throw new InvalidOperationException(
            "TcpChannelHost.DisposeAsync requires CloseAsync to complete synchronously on the owner loop. " +
            "Await CloseAsync() while driving the loop, then call ShutdownIoAsync().");
    }

    closeAwaiter.GetResult();

    var shutdownAwaiter = ShutdownIoAsync().GetAwaiter();
    while (!shutdownAwaiter.IsCompleted)
    {
        ownerLoop.Tick();
        if (!shutdownAwaiter.IsCompleted)
        {
            ownerLoop.WaitForWorkOrTimer(CancellationToken.None);
        }
    }

    shutdownAwaiter.GetResult();
    return ValueTask.CompletedTask;
}
```

- [ ] **Step 5: Run shared transport tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "TcpChannelHostOptionsTests|IChannelPipelineFactoryTests|LoopInboundHandlerTests|TcpChannelHostTests"
```

Expected: pass.

---

## Task 6: Add GameServer Frame Codec Tests

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrame.cs`
- Create: `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrameDecoder.cs`
- Create: `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrameEncoder.cs`
- Create: `Tests/CRPC.Tests/LordUnion/GameServerFrameCodecTests.cs`

- [ ] **Step 1: Write codec parity tests**

Create `Tests/CRPC.Tests/LordUnion/GameServerFrameCodecTests.cs`:

```csharp
using DotNetty.Buffers;
using DotNetty.Transport.Channels.Embedded;
using LordUnion.IntegrationTests.Protocol;

namespace CRPC.Tests.LordUnion;

public sealed class GameServerFrameCodecTests
{
    [Fact]
    public void EncoderMatchesExistingServerPacketFrameBytes()
    {
        var body = new byte[] { 1, 2, 3, 4 };
        var frame = new GameServerFrame(ServerPacketFrame.ClientSendHeaderMagic, body);
        var channel = new EmbeddedChannel(new GameServerFrameEncoder());

        Assert.True(channel.WriteOutbound(frame));
        var buffer = channel.ReadOutbound<IByteBuffer>();
        var bytes = new byte[buffer.ReadableBytes];
        buffer.ReadBytes(bytes);

        Assert.Equal(ServerPacketFrame.EncodeFrame(ServerPacketFrame.ClientSendHeaderMagic, body), bytes);
    }

    [Fact]
    public void DecoderReadsCompleteFrame()
    {
        var body = new byte[] { 9, 8, 7 };
        var packet = ServerPacketFrame.EncodeFrame(1001, body);
        var input = Unpooled.WrappedBuffer(packet);
        var channel = new EmbeddedChannel(new GameServerFrameDecoder(maxBodyLength: 1024));

        Assert.True(channel.WriteInbound(input));
        var frame = channel.ReadInbound<GameServerFrame>();

        Assert.Equal(1001u, frame.Header0);
        Assert.Equal(body, frame.Body);
    }

    [Fact]
    public void DecoderWaitsForPartialBody()
    {
        var body = new byte[] { 5, 6, 7, 8 };
        var packet = ServerPacketFrame.EncodeFrame(1001, body);
        var firstHalf = Unpooled.WrappedBuffer(packet.AsSpan(0, 10).ToArray());
        var secondHalf = Unpooled.WrappedBuffer(packet.AsSpan(10).ToArray());
        var channel = new EmbeddedChannel(new GameServerFrameDecoder(maxBodyLength: 1024));

        Assert.False(channel.WriteInbound(firstHalf));
        Assert.True(channel.WriteInbound(secondHalf));
        var frame = channel.ReadInbound<GameServerFrame>();

        Assert.Equal(1001u, frame.Header0);
        Assert.Equal(body, frame.Body);
    }

    [Fact]
    public void DecoderRejectsNegativeBodyLength()
    {
        var bytes = new byte[ServerPacketFrame.HeaderLength];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), 1001u);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), -1);
        var channel = new EmbeddedChannel(new GameServerFrameDecoder(maxBodyLength: 1024));

        Assert.Throws<InvalidOperationException>(() =>
            channel.WriteInbound(Unpooled.WrappedBuffer(bytes)));
    }
}
```

- [ ] **Step 2: Run codec tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter GameServerFrameCodecTests
```

Expected: fail because the GameServer frame types do not exist.

---

## Task 7: Implement GameServer Frame Codec

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrame.cs`
- Create: `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrameDecoder.cs`
- Create: `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrameEncoder.cs`

- [ ] **Step 1: Add the frame envelope**

Create `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrame.cs`:

```csharp
namespace LordUnion.IntegrationTests.Protocol;

public readonly record struct GameServerFrame(uint Header0, byte[] Body)
{
    public int BodyLength => Body.Length;
}
```

- [ ] **Step 2: Add the decoder**

Create `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrameDecoder.cs`:

```csharp
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace LordUnion.IntegrationTests.Protocol;

public sealed class GameServerFrameDecoder : ByteToMessageDecoder
{
    private readonly int maxBodyLength;

    public GameServerFrameDecoder(int maxBodyLength = 1024 * 1024)
    {
        if (maxBodyLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBodyLength), maxBodyLength, "Max body length must be positive.");
        }

        this.maxBodyLength = maxBodyLength;
    }

    protected override void Decode(IChannelHandlerContext context, IByteBuffer input, List<object> output)
    {
        if (input.ReadableBytes < ServerPacketFrame.HeaderLength)
        {
            return;
        }

        input.MarkReaderIndex();
        var header0 = (uint)input.ReadIntLE();
        var bodyLength = input.ReadIntLE();
        if (bodyLength < 0)
        {
            throw new InvalidOperationException($"Invalid game-server body length {bodyLength}.");
        }

        if (bodyLength > maxBodyLength)
        {
            throw new InvalidOperationException(
                $"Game-server body length {bodyLength} exceeds maximum {maxBodyLength}.");
        }

        if (input.ReadableBytes < bodyLength)
        {
            input.ResetReaderIndex();
            return;
        }

        var body = new byte[bodyLength];
        input.ReadBytes(body);
        output.Add(new GameServerFrame(header0, body));
    }
}
```

- [ ] **Step 3: Add the encoder**

Create `Tests/LordUnion.IntegrationTests/Protocol/GameServerFrameEncoder.cs`:

```csharp
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace LordUnion.IntegrationTests.Protocol;

public sealed class GameServerFrameEncoder : MessageToByteEncoder<GameServerFrame>
{
    protected override void Encode(IChannelHandlerContext context, GameServerFrame message, IByteBuffer output)
    {
        ArgumentNullException.ThrowIfNull(message.Body);

        output.WriteIntLE((int)message.Header0);
        output.WriteIntLE(message.Body.Length);
        output.WriteBytes(message.Body);
    }
}
```

- [ ] **Step 4: Run codec tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter GameServerFrameCodecTests
```

Expected: pass with byte parity against `ServerPacketFrame.EncodeFrame`.

---

## Task 8: Add GameServer Pipeline Factory

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Protocol/GameServerPipelineFactory.cs`
- Create: `Tests/CRPC.Tests/LordUnion/GameServerPipelineFactoryTests.cs`

- [ ] **Step 1: Write pipeline factory test**

Create `Tests/CRPC.Tests/LordUnion/GameServerPipelineFactoryTests.cs`:

```csharp
using CRpc.Async;
using CRpc.Transport;
using DotNetty.Transport.Channels.Embedded;
using LordUnion.IntegrationTests.Protocol;

namespace CRPC.Tests.LordUnion;

public sealed class GameServerPipelineFactoryTests
{
    [Fact]
    public void PipelineDecodesFrameAndPostsToHostLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        object? received = null;
        var host = new TcpChannelHost(loop, new GameServerPipelineFactory())
        {
            InboundMessageReceived = message => received = message
        };
        var channel = new EmbeddedChannel();

        host.PipelineFactory.Configure(channel.Pipeline, host);
        var packet = ServerPacketFrame.EncodeFrame(1001, new byte[] { 1, 2, 3 });

        Assert.True(channel.WriteInbound(DotNetty.Buffers.Unpooled.WrappedBuffer(packet)));
        loop.Tick();

        var frame = Assert.IsType<GameServerFrame>(received);
        Assert.Equal(1001u, frame.Header0);
        Assert.Equal(new byte[] { 1, 2, 3 }, frame.Body);
    }
}
```

- [ ] **Step 2: Run pipeline test and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter GameServerPipelineFactoryTests
```

Expected: fail because `GameServerPipelineFactory` does not exist.

- [ ] **Step 3: Implement the pipeline factory**

Create `Tests/LordUnion.IntegrationTests/Protocol/GameServerPipelineFactory.cs`:

```csharp
using CRpc.Transport;
using DotNetty.Transport.Channels;

namespace LordUnion.IntegrationTests.Protocol;

public sealed class GameServerPipelineFactory : IChannelPipelineFactory
{
    private readonly int maxBodyLength;

    public GameServerPipelineFactory(int maxBodyLength = 1024 * 1024)
    {
        this.maxBodyLength = maxBodyLength;
    }

    public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(host);

        pipeline.AddLast("game-server-decoder", new GameServerFrameDecoder(maxBodyLength));
        pipeline.AddLast("game-server-encoder", new GameServerFrameEncoder());
        pipeline.AddLast("loop-ingress", new LoopInboundHandler(host));
    }
}
```

- [ ] **Step 4: Run pipeline test**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter GameServerPipelineFactoryTests
```

Expected: pass.

---

## Task 9: Add GameServerDotNettyTransport Tests

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Sessions/GameServerDotNettyTransport.cs`
- Create: `Tests/CRPC.Tests/LordUnion/GameServerDotNettyTransportTests.cs`

- [ ] **Step 1: Write delivery and send tests**

Create `Tests/CRPC.Tests/LordUnion/GameServerDotNettyTransportTests.cs`:

```csharp
using CRpc.Async;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

public sealed class GameServerDotNettyTransportTests
{
    [Fact]
    public void DeliverFrameDecodesProtocolMessageOnSessionLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var codec = new ServerProtocolCodec();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new GameServerDotNettyTransport(codec);
        transport.BindIncomingHandler(session, codec);
        var packet = codec.EncodeClientRequest(new TKMobileReqMsg { Param = 7 });
        var requestMessage = codec.DecodePacket(packet);
        var frame = new GameServerFrame(requestMessage.Header0, packet.Skip(ServerPacketFrame.HeaderLength).ToArray());

        transport.DeliverFrameForTesting(frame);
        loop.Tick();

        var received = Assert.Single(session.ReceivedMessages);
        Assert.Equal(SessionMessageDirection.Received, received.Direction);
        Assert.Equal(requestMessage.Header0, received.Header0);
    }

    [Fact]
    public void BuildOutboundFrameForTestingPreservesExistingPacketBytes()
    {
        var codec = new ServerProtocolCodec();
        var packet = codec.EncodeClientRequest(new TKMobileReqMsg { Param = 11 });
        var transport = new GameServerDotNettyTransport(codec);

        var frame = transport.BuildOutboundFrameForTesting(packet);
        var encoded = ServerPacketFrame.EncodeFrame(frame.Header0, frame.Body);

        Assert.Equal(packet, encoded);
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter GameServerDotNettyTransportTests
```

Expected: fail because `GameServerDotNettyTransport` does not exist.

---

## Task 10: Implement GameServerDotNettyTransport

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Sessions/GameServerDotNettyTransport.cs`

- [ ] **Step 1: Implement transport state and binding**

Create `Tests/LordUnion.IntegrationTests/Sessions/GameServerDotNettyTransport.cs`:

```csharp
using CRpc.Async;
using CRpc.Transport;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Sessions;

public sealed class GameServerDotNettyTransport : IGameServerTransport, IAsyncDisposable
{
    private readonly ServerProtocolCodec codec;
    private TcpChannelHost? host;
    private AccountSession? session;

    public GameServerDotNettyTransport(ServerProtocolCodec? codec = null)
    {
        this.codec = codec ?? new ServerProtocolCodec();
    }

    public void BindIncomingHandler(AccountSession session, ServerProtocolCodec codec)
    {
        _ = codec;
        this.session = session ?? throw new ArgumentNullException(nameof(session));
    }
}
```

- [ ] **Step 2: Implement connect/send/disconnect**

Add these methods to `GameServerDotNettyTransport`:

```csharp
public CRpcTask ConnectAsync(
    ServerConfig server,
    TimeSpan timeout,
    CRpcLoop loop,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(server);
    ArgumentNullException.ThrowIfNull(loop);
    _ = cancellationToken;

    var options = new TcpChannelHostOptions
    {
        ConnectTimeoutSeconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds)),
        LoggingName = "game-server"
    };

    return ConnectCoreAsync(server, loop, options);
}

private async CRpcTask ConnectCoreAsync(ServerConfig server, CRpcLoop loop, TcpChannelHostOptions options)
{
    host = new TcpChannelHost(loop, new GameServerPipelineFactory(), options)
    {
        InboundMessageReceived = HandleInboundMessage,
        ChannelBecameInactive = HandleChannelInactive,
        ChannelExceptionCaught = HandleChannelException
    };

    await host.ConnectAsync(server.Host, server.Port);
}

public CRpcTask SendAsync(byte[] packet, CRpcLoop loop)
{
    ArgumentNullException.ThrowIfNull(packet);
    ArgumentNullException.ThrowIfNull(loop);

    if (host is null)
    {
        throw new InvalidOperationException("Transport is not connected.");
    }

    var frame = BuildOutboundFrame(packet);
    return host.WriteAndFlushAsync(frame);
}

public CRpcTask DisconnectAsync(CRpcLoop loop)
{
    ArgumentNullException.ThrowIfNull(loop);

    if (host is null)
    {
        return CRpcTask.CompletedTask(loop);
    }

    return host.CloseAsync();
}
```

- [ ] **Step 3: Implement frame conversion and inbound handling**

Add these methods to `GameServerDotNettyTransport`:

```csharp
private GameServerFrame BuildOutboundFrame(byte[] packet)
{
    if (packet.Length < ServerPacketFrame.HeaderLength)
    {
        throw new ArgumentException(
            $"Packet must include the {ServerPacketFrame.HeaderLength}-byte game-server header.",
            nameof(packet));
    }

    var frame = ServerPacketFrame.DecodeHeader(packet.AsSpan(0, ServerPacketFrame.HeaderLength));
    var expectedLength = ServerPacketFrame.HeaderLength + frame.BodyLength;
    if (packet.Length != expectedLength)
    {
        throw new ArgumentException(
            $"Packet length {packet.Length} does not match header length {expectedLength}.",
            nameof(packet));
    }

    var body = packet.AsSpan(ServerPacketFrame.HeaderLength, frame.BodyLength).ToArray();
    return new GameServerFrame(frame.Header0, body);
}

private void HandleInboundMessage(object message)
{
    if (message is not GameServerFrame frame)
    {
        HandleChannelException(new InvalidOperationException(
            $"Unexpected game-server inbound message type '{message.GetType().FullName}'."));
        return;
    }

    var activeSession = session
        ?? throw new InvalidOperationException("Incoming handler is not bound.");

    var packet = ServerPacketFrame.EncodeFrame(frame.Header0, frame.Body);
    var protocolMessage = codec.DecodePacket(
        packet,
        new ProtocolDecodeContext
        {
            AccountAlias = activeSession.Alias,
            Phase = activeSession.CurrentPhase,
        });

    activeSession.DeliverIncomingMessage(protocolMessage);
}

private void HandleChannelInactive()
{
    session?.SetState(AccountSessionState.Failed);
}

private void HandleChannelException(Exception exception)
{
    Console.Error.WriteLine(
        $"GameServerDotNettyTransport: channel failed for account '{session?.Alias ?? "<unbound>"}': {exception.Message}");
    session?.SetState(AccountSessionState.Failed);
}
```

- [ ] **Step 4: Add friend assembly visibility for focused transport tests**

Create `Tests/LordUnion.IntegrationTests/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CRPC.Tests")]
```

- [ ] **Step 5: Add testing helpers as internal methods**

Add these internal helpers to `GameServerDotNettyTransport`:

```csharp
internal GameServerFrame BuildOutboundFrameForTesting(byte[] packet)
{
    return BuildOutboundFrame(packet);
}

internal void DeliverFrameForTesting(GameServerFrame frame)
{
    HandleInboundMessage(frame);
}
```

- [ ] **Step 6: Run transport tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter GameServerDotNettyTransportTests
```

Expected: pass after any small compile fixes required by existing `CRpcTask` helper availability.

---

## Task 11: Switch LordUnion Default Live Transport

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`
- Read/reference: `Tests/LordUnion.IntegrationTests/Scenarios/ScenarioRunOptions.cs`
- Read/reference: `Tests/LordUnion.IntegrationTests/Sessions/GameServerTcpTransport.cs`

- [ ] **Step 1: Locate current live transport factory**

Find the method currently returning the live transport. It is expected to look like:

```csharp
public IGameServerTransport CreateTransport(AccountSession session, AccountConfig account)
{
    return new GameServerTcpTransport(codec);
}
```

- [ ] **Step 2: Replace default live transport with DotNetty transport**

Change only the default live factory to:

```csharp
public IGameServerTransport CreateTransport(AccountSession session, AccountConfig account)
{
    return new GameServerDotNettyTransport(codec);
}
```

Do not add a CLI flag or permanent config switch. `ScenarioRunOptions.TransportFactory` already exists for tests that need injection.

- [ ] **Step 3: Keep the old transport source file**

Do not delete `Tests/LordUnion.IntegrationTests/Sessions/GameServerTcpTransport.cs` in this task. It remains available as short-term migration reference.

- [ ] **Step 4: Run scenario unit tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter ThreePlayersOneGameScenarioTests
```

Expected: pass. These tests should continue to use fake transports through `ScenarioRunOptions.TransportFactory`.

---

## Task 12: Full Regression Verification

**Files:**
- No source files expected.

- [ ] **Step 1: Run CRPC unit tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: pass.

- [ ] **Step 2: Run LordUnion live scenario**

Run the existing live command used for the current successful scenario. The command should target `Tests/LordUnion.IntegrationTests` and include the existing live/config arguments.

Expected:

```text
success: true
matchId: non-zero
tableId: non-zero
gameDurationMs: greater than 0
```

- [ ] **Step 3: Compare the new report against baseline**

Compare the new report against `scenario-report-20260527T105628Z.json`:

```text
login succeeds for all three accounts
signup succeeds for all three accounts
enterMatch completes for all three accounts
game completes with a winSeat
```

Duration differences are acceptable because the live server and bot pacing vary.

- [ ] **Step 4: If live fails, classify before changing code**

Classify the failure using the report:

```text
connect failure       -> inspect DotNetty host / pipeline setup
decode failure        -> inspect GameServerFrameDecoder byte parity
send failure          -> inspect GameServerFrameEncoder and packet conversion
login/signup failure  -> compare sent packet bytes with ServerPacketFrame output
enter/game timeout    -> inspect received messages before changing transport
```

Do not change `CRpcClient` while investigating these failures.

---

## Task 13: Migration Cleanup After Live Success

**Files:**
- Delete: `Tests/LordUnion.IntegrationTests/Sessions/GameServerTcpTransport.cs`
- Modify: tests only if they directly reference `GameServerTcpTransport`

- [ ] **Step 1: Confirm cleanup gate is satisfied**

Proceed only after:

```text
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

passes and at least one DotNetty-backed live scenario report has:

```json
"success": true
```

- [ ] **Step 2: Delete old TcpClient transport**

Delete `Tests/LordUnion.IntegrationTests/Sessions/GameServerTcpTransport.cs`.

- [ ] **Step 3: Remove references to the old transport**

Search for:

```text
GameServerTcpTransport
```

Expected after cleanup: no source references remain outside historical docs or plans.

- [ ] **Step 4: Run final regression**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: pass.

---

## Out of Scope

- Do not modify `CRPC/Rpc/CRpc/Client/CRpcClient.cs`.
- Do not modify `CRPC/Rpc/CRpc/Codec/CRpcMessageEncoder.cs`.
- Do not modify `CRPC/Rpc/CRpc/Codec/CRpcMessageDecoder.cs`.
- Do not map LordUnion `header0`, `gameId`, `tourneyId`, or message kind into `serviceId` / `methodId`.
- Do not add a permanent live-runner transport selector unless a later design explicitly requires it.
- Do not introduce `System.Threading.Tasks.Task` into project async APIs. `Task` remains acceptable only at external DotNetty/BCL interop boundaries and must be marshaled back through `CRpcTask.FromTask(..., ownerLoop)`.
- Do not commit automatically. Ask the user before creating any git commit.

---

## Self-Review Checklist

- Spec coverage: This plan implements the agreed first delivery: shared `CRPC/Transport` foundation plus LordUnion DotNetty transport, without touching `CRpcClient`.
- Placeholder scan: No implementation step uses unresolved placeholders. Test code includes explicit assertions and expected outcomes.
- Type consistency: `TcpChannelHost`, `TcpChannelHostOptions`, `IChannelPipelineFactory`, `LoopInboundHandler`, `GameServerFrame`, `GameServerFrameDecoder`, `GameServerFrameEncoder`, `GameServerPipelineFactory`, and `GameServerDotNettyTransport` are introduced before use.
- Scope check: The plan intentionally excludes `CRpcClient` migration and frames it as a future phase after LordUnion live verification.
- Verification: Unit tests precede implementation tasks, and live verification is required before cleanup deletes the old TcpClient transport.
