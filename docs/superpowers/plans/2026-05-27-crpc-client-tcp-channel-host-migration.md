# CRpcClient TcpChannelHost Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate `CRpcClient` to use the shared `TcpChannelHost` transport while preserving its public API and RPC behavior.

**Architecture:** `TcpChannelHost` owns DotNetty lifecycle and posts inbound events to the owner `CRpcLoop`. `CRpcClient` owns RPC semantics: request IDs, pending calls, timeouts, response matching, and failure completion. A new `CRpcClientPipelineFactory` wires CRPC codecs and `LoopInboundHandler` into the shared host.

**Tech Stack:** C#/.NET 8, xUnit, DotNetty, custom `CRpcTask`/`CRpcLoop`, existing `CRPC/Transport` abstractions.

**User Constraint:** Do not commit or merge automatically. Implementation tasks intentionally omit git commit steps.

---

## Spec Reference

Design spec: `docs/superpowers/specs/2026-05-27-crpc-client-tcp-channel-host-migration-design.md`

## File Structure

- `CRPC/Transport/TcpChannelHost.cs`
  - Add stale-channel filtering for inactive and exception events.
  - Keep public callbacks unchanged.
- `CRPC/Transport/LoopInboundHandler.cs`
  - Pass `context.Channel` to host inactive/exception methods.
- `CRPC/Rpc/CRpc/Client/CRpcClientPipelineFactory.cs`
  - New CRPC client pipeline factory.
  - Owns CRPC client protocol handlers only.
- `CRPC/Rpc/CRpc/Client/CRpcClient.cs`
  - Replace direct DotNetty bootstrap/IO group/channel ownership with `TcpChannelHost`.
  - Preserve public API and pending-call behavior.
- `CRPC/Rpc/CRpc/Client/CRpcClientHandler.cs`
  - Delete after migration.
- `Tests/CRPC.Tests/Transport/TcpChannelHostTests.cs`
  - Add stale-channel filtering tests.
- `Tests/CRPC.Tests/CRpcClientPipelineFactoryTests.cs`
  - New tests for CRPC client pipeline composition.
- `Tests/CRPC.Tests/CRpcClientTests.cs`
  - Update reflection helpers and channel-loss tests for host-based client.

## Task 1: TcpChannelHost Stale Channel Filtering Tests

**Files:**
- Modify: `Tests/CRPC.Tests/Transport/TcpChannelHostTests.cs`

- [ ] **Step 1: Add helper methods to set host channel and invoke internal callbacks**

Add these helpers near the bottom of `TcpChannelHostTests`, before `EmptyPipelineFactory`:

```csharp
private static void SetHostChannel(TcpChannelHost host, IChannel channel)
{
    var field = typeof(TcpChannelHost).GetField(
        "channel",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(field);
    field!.SetValue(host, channel);
}

private static void DrainOwnerLoop(CRpcLoop loop)
{
    loop.Tick();
}
```

- [ ] **Step 2: Add inactive stale-channel test**

Add this test to `TcpChannelHostTests`:

```csharp
[Fact]
public void StaleChannelInactiveDoesNotInvokeCallback()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var currentChannel = new EmbeddedChannel();
    var staleChannel = new EmbeddedChannel();
    var callbackCount = 0;
    var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
    {
        ChannelBecameInactive = () => callbackCount++
    };
    SetHostChannel(host, currentChannel);

    host.PostChannelInactive(staleChannel);
    DrainOwnerLoop(loop);

    Assert.Equal(0, callbackCount);
}
```

- [ ] **Step 3: Add current inactive test**

Add this test to `TcpChannelHostTests`:

```csharp
[Fact]
public void CurrentChannelInactiveInvokesCallback()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var currentChannel = new EmbeddedChannel();
    var callbackCount = 0;
    var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
    {
        ChannelBecameInactive = () => callbackCount++
    };
    SetHostChannel(host, currentChannel);

    host.PostChannelInactive(currentChannel);
    DrainOwnerLoop(loop);

    Assert.Equal(1, callbackCount);
}
```

- [ ] **Step 4: Add exception stale-channel test**

Add this test to `TcpChannelHostTests`:

```csharp
[Fact]
public void StaleChannelExceptionDoesNotInvokeCallback()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var currentChannel = new EmbeddedChannel();
    var staleChannel = new EmbeddedChannel();
    var callbackCount = 0;
    var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
    {
        ChannelExceptionCaught = _ => callbackCount++
    };
    SetHostChannel(host, currentChannel);

    host.PostChannelException(staleChannel, new InvalidOperationException("boom"));
    DrainOwnerLoop(loop);

    Assert.Equal(0, callbackCount);
}
```

- [ ] **Step 5: Add current exception test**

Add this test to `TcpChannelHostTests`:

```csharp
[Fact]
public void CurrentChannelExceptionInvokesCallback()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var currentChannel = new EmbeddedChannel();
    var expected = new InvalidOperationException("boom");
    Exception? received = null;
    var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
    {
        ChannelExceptionCaught = exception => received = exception
    };
    SetHostChannel(host, currentChannel);

    host.PostChannelException(currentChannel, expected);
    DrainOwnerLoop(loop);

    Assert.Same(expected, received);
}
```

- [ ] **Step 6: Run tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~TcpChannelHostTests" -v q
```

Expected: build fails because `TcpChannelHost.PostChannelInactive` and `PostChannelException` do not yet accept `IChannel`.

## Task 2: TcpChannelHost Stale Channel Filtering Implementation

**Files:**
- Modify: `CRPC/Transport/TcpChannelHost.cs`
- Modify: `CRPC/Transport/LoopInboundHandler.cs`
- Modify: `Tests/CRPC.Tests/Transport/LoopInboundHandlerTests.cs`

- [ ] **Step 1: Update `TcpChannelHost` internal event methods**

Replace:

```csharp
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

with:

```csharp
internal void PostChannelInactive(IChannel eventChannel)
{
    ArgumentNullException.ThrowIfNull(eventChannel);
    ownerLoop.Post(() =>
    {
        if (!ReferenceEquals(channel, eventChannel))
        {
            return;
        }

        ChannelBecameInactive?.Invoke();
    });
}

internal void PostChannelException(IChannel eventChannel, Exception exception)
{
    ArgumentNullException.ThrowIfNull(eventChannel);
    ArgumentNullException.ThrowIfNull(exception);
    ownerLoop.Post(() =>
    {
        if (!ReferenceEquals(channel, eventChannel))
        {
            return;
        }

        ChannelExceptionCaught?.Invoke(exception);
    });
}
```

- [ ] **Step 2: Update `LoopInboundHandler`**

Replace:

```csharp
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
```

with:

```csharp
public override void ChannelInactive(IChannelHandlerContext context)
{
    host.PostChannelInactive(context.Channel);
    base.ChannelInactive(context);
}

public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
{
    host.PostChannelException(context.Channel, exception);
    _ = context.CloseAsync();
}
```

- [ ] **Step 3: Update `LoopInboundHandlerTests` to register embedded channel as current**

In `ExceptionCaughtPostsExceptionToOwnerLoop`, after creating the channel:

```csharp
var channel = new EmbeddedChannel(new LoopInboundHandler(host));
```

set the private host channel using reflection:

```csharp
SetHostChannel(host, channel);
```

Add this helper to `LoopInboundHandlerTests`:

```csharp
private static void SetHostChannel(TcpChannelHost host, IChannel channel)
{
    var field = typeof(TcpChannelHost).GetField(
        "channel",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(field);
    field!.SetValue(host, channel);
}
```

If `ChannelReadPostsInboundMessageToOwnerLoop` does not use inactive/exception callbacks, do not change it.

- [ ] **Step 4: Run focused transport tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~TcpChannelHostTests|FullyQualifiedName~LoopInboundHandlerTests" -v q
```

Expected: all selected tests pass.

## Task 3: CRpcClientPipelineFactory Tests

**Files:**
- Create: `Tests/CRPC.Tests/CRpcClientPipelineFactoryTests.cs`

- [ ] **Step 1: Create failing pipeline factory tests**

Create `Tests/CRPC.Tests/CRpcClientPipelineFactoryTests.cs`:

```csharp
using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Transport;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public sealed class CRpcClientPipelineFactoryTests : CrpcTestBase
{
    [Fact]
    public void ConfigureAddsClientCodecAndLoopInboundHandler()
    {
        var options = new CRpcClientOptions
        {
            HeartbeatIdleSeconds = 17,
            MaxFrameLength = 4096,
            HashLength = 16,
            CompressThreshold = 128,
        };
        var loop = new CRpcLoop();
        var host = new TcpChannelHost(loop, new CRpcClientPipelineFactory(options));
        var channel = new EmbeddedChannel();

        host.PipelineFactory.Configure(channel.Pipeline, host);

        Assert.NotNull(channel.Pipeline.Get<IdleStateHandler>());
        Assert.NotNull(channel.Pipeline.Get<CRpcMessageDecoder>());
        Assert.NotNull(channel.Pipeline.Get<CRpcMessageEncoder>());
        Assert.NotNull(channel.Pipeline.Get<LoopInboundHandler>());
    }

    [Fact]
    public void ConstructorThrowsWhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CRpcClientPipelineFactory(null!));
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcClientPipelineFactoryTests" -v q
```

Expected: build fails because `CRpcClientPipelineFactory` does not exist.

## Task 4: CRpcClientPipelineFactory Implementation

**Files:**
- Create: `CRPC/Rpc/CRpc/Client/CRpcClientPipelineFactory.cs`

- [ ] **Step 1: Add pipeline factory**

Create `CRPC/Rpc/CRpc/Client/CRpcClientPipelineFactory.cs`:

```csharp
using CRpc.Rpc.CRpc.Codec;
using CRpc.Transport;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Client;

internal sealed class CRpcClientPipelineFactory : IChannelPipelineFactory
{
    private readonly CRpcClientOptions options;

    public CRpcClientPipelineFactory(CRpcClientOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(host);

        pipeline.AddLast(
            "timeout",
            new IdleStateHandler(0, 0, options.HeartbeatIdleSeconds));
        pipeline.AddLast(
            "decoder",
            new CRpcMessageDecoder(options.MaxFrameLength, options.HashLength));
        pipeline.AddLast(
            "encoder",
            new CRpcMessageEncoder(options.HashLength, options.CompressThreshold));
        pipeline.AddLast("handler", new LoopInboundHandler(host));
    }
}
```

- [ ] **Step 2: Run pipeline factory tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcClientPipelineFactoryTests" -v q
```

Expected: tests pass.

## Task 5: CRpcClient Host Injection Test Harness

**Files:**
- Modify: `CRPC/Rpc/CRpc/Client/CRpcClient.cs`
- Modify: `Tests/CRPC.Tests/CRpcClientTests.cs`

- [ ] **Step 1: Add internal constructor seam test**

Add this test to `CRpcClientTests`:

```csharp
[Fact]
public void ConstructorAllowsTestHostInjection()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var options = new CRpcClientOptions();
    var host = new TcpChannelHost(loop, new CRpcClientPipelineFactory(options));

    var client = new CRpcClient(loop, options, host);

    Assert.Same(options, client.Options);
}
```

Add these usings if missing:

```csharp
using CRpc.Transport;
```

- [ ] **Step 2: Run test and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~ConstructorAllowsTestHostInjection" -v q
```

Expected: build fails because the internal `CRpcClient` constructor does not exist.

- [ ] **Step 3: Add internal constructor**

In `CRpcClient`, add a private host field:

```csharp
private readonly TcpChannelHost host;
```

Replace the public constructor body with delegation:

```csharp
public CRpcClient(CRpcLoop loop, CRpcClientOptions? options = null)
    : this(
        loop,
        options ?? new CRpcClientOptions(),
        CreateHost(loop, options ?? new CRpcClientOptions()))
{
}
```

Then adjust it to avoid constructing options twice:

```csharp
public CRpcClient(CRpcLoop loop, CRpcClientOptions? options = null)
    : this(loop, options ?? new CRpcClientOptions(), createHost: true)
{
}

private CRpcClient(CRpcLoop loop, CRpcClientOptions options, bool createHost)
    : this(loop, options, CreateHost(loop, options))
{
}
```

Add the internal constructor:

```csharp
internal CRpcClient(CRpcLoop loop, CRpcClientOptions options, TcpChannelHost host)
{
    ArgumentNullException.ThrowIfNull(loop);
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(host);

    ownerLoop = loop;
    this.options = options;
    this.host = host;
}
```

Add `CreateHost`:

```csharp
private static TcpChannelHost CreateHost(CRpcLoop loop, CRpcClientOptions options)
{
    return new TcpChannelHost(
        loop,
        new CRpcClientPipelineFactory(options),
        new TcpChannelHostOptions
        {
            IoThreadCount = options.IoThreadCount,
            ConnectTimeoutSeconds = options.ConnectTimeoutSeconds,
            TcpNoDelay = true,
            LoggingName = "crpc-client",
        });
}
```

Keep old transport fields in place for this task if needed to avoid changing behavior yet.

- [ ] **Step 4: Run constructor test**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~ConstructorAllowsTestHostInjection" -v q
```

Expected: test passes.

## Task 6: Migrate CRpcClient Connect/Close/Shutdown To Host

**Files:**
- Modify: `CRPC/Rpc/CRpc/Client/CRpcClient.cs`
- Modify: `Tests/CRPC.Tests/CRpcClientTests.cs`

- [ ] **Step 1: Replace channel reflection helper with host-channel helper**

In `CRpcClientTests`, replace `SetClientChannel` with:

```csharp
private static void SetClientHostChannel(CRpcClient client, EmbeddedChannel channel)
{
    var hostField = typeof(CRpcClient).GetField(
        "host",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(hostField);
    var host = Assert.IsType<TcpChannelHost>(hostField!.GetValue(client));

    var channelField = typeof(TcpChannelHost).GetField(
        "channel",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(channelField);
    channelField!.SetValue(host, channel);
}
```

Update existing `SetClientChannel(client, channel)` calls to `SetClientHostChannel(client, channel)`.

- [ ] **Step 2: Add host accessor helper for callback tests**

Add this helper to `CRpcClientTests`:

```csharp
private static TcpChannelHost GetClientHost(CRpcClient client)
{
    var hostField = typeof(CRpcClient).GetField(
        "host",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(hostField);
    return Assert.IsType<TcpChannelHost>(hostField!.GetValue(client));
}
```

- [ ] **Step 3: Migrate `ConnectAsync`**

Replace:

```csharp
if (channel is not null)
{
    throw new InvalidOperationException("CRpcClient is already connected.");
}

return ConnectInternalAsync(host, port);
```

with:

```csharp
if (this.host.IsConnected)
{
    throw new InvalidOperationException("CRpcClient is already connected.");
}

return this.host.ConnectAsync(host, port);
```

Remove `ConnectInternalAsync`.

- [ ] **Step 4: Migrate `CloseAsync`**

Replace the current channel-based close logic with:

```csharp
public CRpcTask CloseAsync()
{
    EnsureOwnerLoopThread();

    FailPendingCalls(new ConnectionClosedException("CRpcClient channel was closed."));
    return host.CloseAsync();
}
```

- [ ] **Step 5: Migrate `ShutdownIoAsync`**

Replace:

```csharp
return CRpcTask.FromTask(group.ShutdownGracefullyAsync(), ownerLoop);
```

with:

```csharp
return host.ShutdownIoAsync();
```

- [ ] **Step 6: Remove old direct transport fields and usings**

Remove from `CRpcClient`:

```csharp
private readonly IEventLoopGroup group;
private IChannel? channel;
private readonly Bootstrap bootstrap = new Bootstrap();
```

Remove unused usings:

```csharp
using DotNetty.Handlers.Logging;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels.Sockets;
```

Add:

```csharp
using CRpc.Transport;
```

- [ ] **Step 7: Run client tests and fix compile errors only in migrated paths**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcClientTests" -v q
```

Expected: some tests still fail or fail to compile because `CallAsync`, response callbacks, and channel-loss tests still reference old channel behavior. Do not change public API.

## Task 7: Migrate CRpcClient CallAsync And Response Callbacks

**Files:**
- Modify: `CRPC/Rpc/CRpc/Client/CRpcClient.cs`
- Modify: `Tests/CRPC.Tests/CRpcClientTests.cs`

- [ ] **Step 1: Add host callbacks in constructor**

Update the internal constructor to assign callbacks through object initializer when creating the host, or create the host after fields are assigned.

Use this pattern in the public construction path:

```csharp
private static TcpChannelHost CreateHost(CRpcLoop loop, CRpcClientOptions options)
{
    return new TcpChannelHost(
        loop,
        new CRpcClientPipelineFactory(options),
        new TcpChannelHostOptions
        {
            IoThreadCount = options.IoThreadCount,
            ConnectTimeoutSeconds = options.ConnectTimeoutSeconds,
            TcpNoDelay = true,
            LoggingName = "crpc-client",
        });
}
```

Then add a method on `CRpcClient`:

```csharp
private void ConfigureHostCallbacks()
{
    host.InboundMessageReceived = OnHostInboundMessage;
    host.ChannelBecameInactive = OnHostChannelInactive;
    host.ChannelExceptionCaught = OnHostChannelException;
}
```

If `TcpChannelHost` callbacks are currently `init` only, change them to settable properties:

```csharp
public Action<object>? InboundMessageReceived { get; set; }

public Action? ChannelBecameInactive { get; set; }

public Action<Exception>? ChannelExceptionCaught { get; set; }
```

Call `ConfigureHostCallbacks()` from all `CRpcClient` constructors after `this.host` is assigned.

- [ ] **Step 2: Replace `OnReceiveResponse` with host inbound handling**

Keep an internal response entry point for existing tests if useful, but route host messages through:

```csharp
private void OnHostInboundMessage(object message)
{
    if (message is not CRpcMessage response)
    {
        OnHostChannelException(new InvalidOperationException(
            $"CRpcClient received unexpected inbound message type '{message.GetType().FullName}'."));
        return;
    }

    CompleteReceiveResponse(response);
}
```

Change `OnReceiveResponse` to:

```csharp
internal void OnReceiveResponse(CRpcMessage message)
{
    ownerLoop.Post(() => CompleteReceiveResponse(message));
}
```

This preserves existing tests that explicitly post a response from another thread. Host callbacks already run on the owner loop, so `OnHostInboundMessage` calls `CompleteReceiveResponse` directly.

- [ ] **Step 3: Replace channel-loss methods with host-loss methods**

Replace old `OnChannelInactive(IChannel inactiveChannel)`, `OnChannelException(IChannel faultedChannel, Exception exception)`, and `OnChannelLost(...)` with:

```csharp
private void OnHostChannelInactive()
{
    FailPendingCalls(new ConnectionClosedException("CRpcClient channel became inactive."));
}

private void OnHostChannelException(Exception exception)
{
    ArgumentNullException.ThrowIfNull(exception);
    FailPendingCalls(new ConnectionClosedException(
        "CRpcClient channel encountered an exception.",
        exception));
}
```

Do not post to the loop here; `TcpChannelHost` already invokes callbacks on the owner loop.

- [ ] **Step 4: Migrate `CallAsync` connection check**

Replace:

```csharp
var currentChannel = channel
    ?? throw new InvalidOperationException("CRpcClient is not connected.");
```

with:

```csharp
if (!host.IsConnected)
{
    throw new InvalidOperationException("CRpcClient is not connected.");
}
```

- [ ] **Step 5: Preserve fire-and-forget write semantics**

Replace `__Send(IChannel currentChannel, ...)` with:

```csharp
private void __Send(long reqSeq, ushort serviceId, ushort methodId, byte[] bytes)
{
    CRpcMessageHeader header = CRpcMessageHeader.valueOf(CRpcMessageState.STATE_NONE, 0, reqSeq, serviceId, methodId);
    header.addState(CRpcMessageState.NONE_ENCRYPT);
    CRpcMessage req = CRpcMessage.valueOf(header, bytes);
    Console.WriteLine($"*********CallAsync send");
    var writeTask = host.WriteAndFlushAsync(req);
    var awaiter = writeTask.GetAwaiter();
    if (awaiter.IsCompleted)
    {
        awaiter.GetResult();
    }
}
```

Update call site:

```csharp
__Send(reqSeq, serviceId, methodId, body);
```

Do not `await` the write task and do not add a continuation.

- [ ] **Step 6: Update inactive and exception tests**

In `ChannelInactiveFailsPendingCalls`, replace embedded `CRpcClientHandler` usage with host callback invocation:

```csharp
var channel = new EmbeddedChannel();
SetClientHostChannel(client, channel);
var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);

GetClientHost(client).PostChannelInactive(channel);
loop.Tick();
```

In `ExceptionCaughtFailsPendingCalls`, replace embedded `CRpcClientHandler` usage with:

```csharp
var channel = new EmbeddedChannel();
SetClientHostChannel(client, channel);
var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);

GetClientHost(client).PostChannelException(channel, pipelineException);
loop.Tick();
```

In `StaleChannelInactiveDoesNotFailCurrentChannelPendingCalls`, replace old handler usage with:

```csharp
var oldChannel = new EmbeddedChannel();
var newChannel = new EmbeddedChannel();
SetClientHostChannel(client, newChannel);
var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
var awaiter = task.GetAwaiter();

GetClientHost(client).PostChannelInactive(oldChannel);
loop.Tick();

Assert.False(awaiter.IsCompleted);
```

- [ ] **Step 7: Run client tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcClientTests" -v q
```

Expected: `CRpcClientTests` pass.

## Task 8: Delete CRpcClientHandler

**Files:**
- Delete: `CRPC/Rpc/CRpc/Client/CRpcClientHandler.cs`
- Modify: tests only if they still reference `CRpcClientHandler`

- [ ] **Step 1: Search for remaining references**

Run:

```bash
rg "CRpcClientHandler" CRPC Tests
```

Expected before deletion: references only in `CRpcClientHandler.cs`, or no references if tests were already updated.

- [ ] **Step 2: Delete handler**

Delete:

```text
CRPC/Rpc/CRpc/Client/CRpcClientHandler.cs
```

- [ ] **Step 3: Verify no source references remain**

Run:

```bash
rg "CRpcClientHandler" CRPC Tests
```

Expected: no source references.

## Task 9: Local Pipeline Response Integration Test

**Files:**
- Modify: `Tests/CRPC.Tests/CRpcClientTests.cs`

- [ ] **Step 1: Add test that host callback completes pending call**

Add this test to `CRpcClientTests`:

```csharp
[Fact]
public void HostInboundMessageCompletesPendingCall()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();

    var client = new CRpcClient(loop);
    SetClientHostChannel(client, new EmbeddedChannel());
    var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
    var awaiter = task.GetAwaiter();
    CRpcMessage? result = null;
    awaiter.OnCompleted(() => result = awaiter.GetResult());

    GetClientHost(client).PostInboundMessage(CreateResponse(reqSequence: 1));
    loop.Tick();

    Assert.True(awaiter.IsCompleted);
    Assert.Same(result, awaiter.GetResult());
}
```

- [ ] **Step 2: Add unexpected inbound type test**

Add this test to `CRpcClientTests`:

```csharp
[Fact]
public void UnexpectedHostInboundMessageFailsPendingCalls()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();

    var client = new CRpcClient(loop);
    SetClientHostChannel(client, new EmbeddedChannel());
    var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 5000);
    var awaiter = task.GetAwaiter();

    GetClientHost(client).PostInboundMessage(new object());
    loop.Tick();

    var exception = Assert.Throws<ConnectionClosedException>(() => awaiter.GetResult());
    Assert.IsType<InvalidOperationException>(exception.InnerException);
}
```

- [ ] **Step 3: Run focused client tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcClientTests" -v q
```

Expected: all selected tests pass.

## Task 10: Full Regression Verification

**Files:**
- No source edits expected.

- [ ] **Step 1: Run transport and client focused tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~TcpChannelHostTests|FullyQualifiedName~LoopInboundHandlerTests|FullyQualifiedName~CRpcClientPipelineFactoryTests|FullyQualifiedName~CRpcClientTests" -v q
```

Expected: all selected tests pass.

- [ ] **Step 2: Run full CRPC test suite**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj -v q
```

Expected: all tests pass.

- [ ] **Step 3: Search for forbidden or removed patterns**

Run:

```bash
rg "CRpcClientHandler|new Bootstrap|MultithreadEventLoopGroup|IEventLoopGroup" CRPC/Rpc/CRpc/Client Tests/CRPC.Tests
```

Expected:

- no `CRpcClientHandler`
- no `new Bootstrap` in `CRPC/Rpc/CRpc/Client`
- no `MultithreadEventLoopGroup` in `CRPC/Rpc/CRpc/Client`
- no `IEventLoopGroup` in `CRPC/Rpc/CRpc/Client`

- [ ] **Step 4: Confirm public API is unchanged**

Inspect `CRPC/Rpc/CRpc/Client/CRpcClient.cs` and confirm these signatures still exist:

```csharp
public CRpcTask<IChannel> ConnectAsync(string host, int port)
public CRpcTask<CRpcMessage> CallAsync(ushort serviceId, ushort methodId, byte[] body, int timeout)
public CRpcTask CloseAsync()
public CRpcTask ShutdownIoAsync()
public ValueTask DisposeAsync()
```

Expected: signatures unchanged.

## Out of Scope

- Do not change `IRpcClient`.
- Do not change `CRpcReference`, `CRpcReferenceOfT`, or proxy activation behavior.
- Do not change `CRpcMessageEncoder` or `CRpcMessageDecoder` protocol semantics.
- Do not add a real CRPC live-server requirement.
- Do not add asynchronous write-failure continuation behavior.
- Do not replace existing `Console.WriteLine` logging in this migration.
- Do not commit or merge automatically.

## Self-Review Checklist

- Spec coverage: every design requirement maps to at least one task.
- Placeholder scan: no placeholder markers or vague implementation-only steps.
- Type consistency: plan uses `TcpChannelHost`, `CRpcClientPipelineFactory`, `LoopInboundHandler`, `CRpcMessage`, and `ConnectionClosedException` consistently.
- Scope check: plan only migrates `CRpcClient` to the shared host and does not expand into RPC API redesign.
