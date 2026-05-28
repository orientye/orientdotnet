# CRpc Server Push Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add fire-and-forget CRpc server push over existing TCP connections, with generated `XxxServiceBase` push helpers and generated `XxxClientBase` push handlers.

**Architecture:** Keep request/response RPC and push routing separate. The server owns loop-bound `CRpcConnection` objects and exposes `CRpcContext.Connection`; generated service bases submit `STATE_PUSH` messages to a connection. The client routes `STATE_RESPONSE` to pending calls and `STATE_PUSH` to registered generated handlers on the owner `CRpcLoop`.

**Tech Stack:** C# / .NET 8, DotNetty, `CRpcLoop` / `CRpcTask`, Google.Protobuf, xUnit.

**Spec reference:** `docs/superpowers/specs/2026-05-28-crpc-server-push-design.md`

**Repository rule:** Do not create commits unless the user explicitly requests them.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `CRpc/Rpc/CRpc/Codec/CRpcMessageState.cs` | Keep `STATE_PUSH = 1 << 2`; add tests around its routing semantics rather than changing the value. |
| `CRpc/Rpc/CRpc/Server/CRpcConnection.cs` | New loop-owned server connection wrapper with `ConnectionId`, `IsActive`, and `SendPushAsync`. |
| `CRpc/Rpc/CRpc/Server/CRpcConnectionRegistry.cs` | New loop-owned registry for active server connections. |
| `CRpc/Rpc/CRpc/Server/CRpcContext.cs` | Carry the current `CRpcConnection` for request handlers. |
| `CRpc/Rpc/CRpc/Server/CRpcServer.cs` | Own the connection registry and create server handlers with access to it. |
| `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs` | Register/unregister connections and invoke services with a connection-aware context. |
| `CRpc/Rpc/CRpc/Client/CRpcPushContext.cs` | New client-side push handler context. |
| `CRpc/Rpc/CRpc/Client/CRpcPushHandler.cs` | New delegate and registration record types for push dispatch. |
| `CRpc/Rpc/CRpc/Client/ICRpcGeneratedClient.cs` | New generated-client binding contract. |
| `CRpc/Rpc/CRpc/Client/CRpcClient.cs` | Add push registration, inbound state routing, unhandled push callback, and push exception callback. |
| `CRpc/Rpc/CRpc/Client/CRpcProxyActivator.cs` | Bind generated clients through `ICRpcGeneratedClient` instead of reflecting for `__client`. |
| `CRpc/Rpc/IRPCClient.cs` | Add push handler registration API to the client abstraction. |
| `CRpc/Rpc/CRpc/Protobuf/crpc-options.proto` | Add `server_push` method option. |
| `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcOptions.cs` | Regenerate or update option extension constants for `server_push`. |
| `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcGen.cs` | Generate `XxxServiceBase`, `XxxClientBase`, push helpers, push handlers, and validation. |
| `Tests/CRPC.Tests/CRPC.Tests.csproj` | Reference the protobuf plugin project so generator tests can call `CRpcGen`. |
| `Example/HelloWorld/Server/HellowolrdServiceImpl.cs` | Migrate server example to inherit `GreeterServiceBase`. |
| `Example/HelloWorld/Client/HelloworldClient.cs` | Regenerate/update generated client base shape. |
| `Example/HelloWorld/Server/HelloworldService.cs` | Regenerate/update generated service base shape. |
| `Example/HelloWorld/Client/Program.cs` | Keep `CRpcReference.For<GreeterClient>()` usage working with user-owned concrete client. |
| `Tests/CRPC.Tests/CRpcClientTests.cs` | Add push routing, handler, unhandled, exception, and pending-call isolation tests. |
| `Tests/CRPC.Tests/CRpcServerHandlerTests.cs` | Add connection lifecycle and context connection tests. |
| `Tests/CRPC.Tests/CRpcConnectionTests.cs` | New focused tests for `CRpcConnection.SendPushAsync`. |
| `Tests/CRPC.Tests/CRpcReferenceTests.cs` | Update proxy activator tests for `ICRpcGeneratedClient`. |
| `Tests/CRPC.Tests/CRpcGeneratorTests.cs` | New generator tests for naming, push options, validation, and generated snippets. |

---

## Task 1: Client Binding Contract

**Files:**
- Create: `CRpc/Rpc/CRpc/Client/ICRpcGeneratedClient.cs`
- Modify: `CRpc/Rpc/CRpc/Client/CRpcProxyActivator.cs`
- Modify: `Tests/CRPC.Tests/CRpcReferenceTests.cs`

- [ ] **Step 1: Write failing proxy activator tests**

Replace the first two tests in `Tests/CRPC.Tests/CRpcReferenceTests.cs` with tests for the new binding contract:

```csharp
[Fact]
public void ProxyActivatorBindsGeneratedClientThroughInterface()
{
    var rpcClient = new RecordingRpcClient();

    var proxy = CRpcProxyActivator.Create<TestGeneratedClient>(rpcClient);

    Assert.Same(rpcClient, proxy.Client);
    Assert.Equal(1, proxy.BindCount);
}

[Fact]
public void ProxyActivatorRejectsTypeWithoutGeneratedClientInterface()
{
    var exception = Assert.Throws<InvalidOperationException>(
        () => CRpcProxyActivator.Create<InvalidGeneratedClient>(new RecordingRpcClient()));

    Assert.Contains(nameof(ICRpcGeneratedClient), exception.Message);
}
```

Update the test helper classes in the same file:

```csharp
private sealed class TestGeneratedClient : ICRpcGeneratedClient
{
    public IRpcClient? Client { get; private set; }

    public int BindCount { get; private set; }

    public void BindRpcClient(IRpcClient client)
    {
        Client = client;
        BindCount++;
    }
}

private sealed class InvalidGeneratedClient
{
}
```

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcReferenceTests
```

Expected: build fails because `ICRpcGeneratedClient` does not exist, or tests fail because `CRpcProxyActivator` still looks for `__client`.

- [ ] **Step 3: Add `ICRpcGeneratedClient`**

Create `CRpc/Rpc/CRpc/Client/ICRpcGeneratedClient.cs`:

```csharp
using CRpc.Rpc;

namespace CRpc.Rpc.CRpc.Client;

public interface ICRpcGeneratedClient
{
    void BindRpcClient(IRpcClient client);
}
```

- [ ] **Step 4: Update `CRpcProxyActivator`**

Replace `CRpcProxyActivator.Create<TProxy>` with:

```csharp
using CRpc.Rpc;

namespace CRpc.Rpc.CRpc.Client;

public static class CRpcProxyActivator
{
    public static TProxy Create<TProxy>(IRpcClient rpcClient)
        where TProxy : class, new()
    {
        ArgumentNullException.ThrowIfNull(rpcClient);

        var proxy = new TProxy();
        if (proxy is not ICRpcGeneratedClient generatedClient)
        {
            throw new InvalidOperationException(
                $"{typeof(TProxy).FullName} must implement {nameof(ICRpcGeneratedClient)}.");
        }

        generatedClient.BindRpcClient(rpcClient);
        return proxy;
    }
}
```

- [ ] **Step 5: Run focused tests and verify pass**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcReferenceTests
```

Expected: all `CRpcReferenceTests` pass.

---

## Task 2: Client Push Dispatch Runtime

**Files:**
- Create: `CRpc/Rpc/CRpc/Client/CRpcPushContext.cs`
- Create: `CRpc/Rpc/CRpc/Client/CRpcPushHandler.cs`
- Modify: `CRpc/Rpc/IRPCClient.cs`
- Modify: `CRpc/Rpc/CRpc/Client/CRpcClient.cs`
- Modify: `Tests/CRPC.Tests/CRpcClientTests.cs`

- [ ] **Step 1: Write failing push dispatch tests**

Add these tests to `Tests/CRPC.Tests/CRpcClientTests.cs`:

```csharp
[Fact]
public void PushMessageDispatchesRegisteredHandlerOnOwnerLoop()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var loopThreadId = Environment.CurrentManagedThreadId;
    var client = new CRpcClient(loop);

    CRpcPushContext? capturedContext = null;
    byte[]? capturedBody = null;
    int? handlerThreadId = null;
    client.RegisterPushHandler(
        10,
        20,
        (context, body) =>
        {
            capturedContext = context;
            capturedBody = body;
            handlerThreadId = Environment.CurrentManagedThreadId;
            return CRpcTask.CompletedTask(context.Loop);
        });

    var push = CreatePush(serviceId: 10, methodId: 20, body: [1, 2, 3]);
    var worker = new Thread(() => client.OnReceiveResponse(push));
    worker.Start();
    worker.Join();

    Assert.Null(capturedContext);

    loop.Tick();

    Assert.NotNull(capturedContext);
    Assert.Same(loop, capturedContext!.Loop);
    Assert.Equal(10, capturedContext.ServiceId);
    Assert.Equal(20, capturedContext.MethodId);
    Assert.Equal([1, 2, 3], capturedBody);
    Assert.Equal(loopThreadId, handlerThreadId);
}

[Fact]
public void PushMessageDoesNotCompletePendingCallWithSameSequence()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var client = new CRpcClient(loop);
    SetClientHostChannel(client, new EmbeddedChannel());
    var task = client.CallAsync(10, 20, Array.Empty<byte>(), timeout: 5000);
    var awaiter = task.GetAwaiter();

    client.OnReceiveResponse(CreatePush(10, 20, Array.Empty<byte>(), reqSequence: 1));
    loop.Tick();

    Assert.False(awaiter.IsCompleted);
    Assert.Equal(1, GetPendingCallCount(client));
}

[Fact]
public void UnknownPushInvokesUnhandledCallbackAndKeepsConnectionUsable()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var client = new CRpcClient(loop);
    CRpcPushContext? unhandled = null;
    client.OnUnhandledPush = context => unhandled = context;

    client.OnReceiveResponse(CreatePush(77, 88, [9]));
    loop.Tick();

    Assert.NotNull(unhandled);
    Assert.Equal(77, unhandled!.ServiceId);
    Assert.Equal(88, unhandled.MethodId);
}

[Fact]
public void PushHandlerExceptionInvokesExceptionCallback()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var client = new CRpcClient(loop);
    var expected = new InvalidOperationException("push failed");
    Exception? captured = null;
    CRpcPushContext? capturedContext = null;

    client.RegisterPushHandler(
        1,
        2,
        (context, body) => throw expected);
    client.OnPushException = (context, exception) =>
    {
        capturedContext = context;
        captured = exception;
    };

    client.OnReceiveResponse(CreatePush(1, 2, Array.Empty<byte>()));
    loop.Tick();

    Assert.Same(expected, captured);
    Assert.Equal(1, capturedContext!.ServiceId);
    Assert.Equal(2, capturedContext.MethodId);
}
```

Add this helper near the existing response helpers:

```csharp
private static CRpcMessage CreatePush(
    ushort serviceId,
    ushort methodId,
    byte[] body,
    long reqSequence = 0)
{
    var header = CRpcMessageHeader.valueOf(
        CRpcMessageState.STATE_PUSH,
        resultCode: 0,
        sn: reqSequence,
        module: serviceId,
        command: methodId);
    header.addState(CRpcMessageState.NONE_ENCRYPT);
    return CRpcMessage.valueOf(header, body);
}
```

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcClientTests
```

Expected: build fails because push types and APIs do not exist.

- [ ] **Step 3: Add push context and handler delegate**

Create `CRpc/Rpc/CRpc/Client/CRpcPushContext.cs`:

```csharp
using CRpc.Async;

namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcPushContext
{
    public CRpcPushContext(CRpcLoop loop, ushort serviceId, ushort methodId)
    {
        Loop = loop ?? throw new ArgumentNullException(nameof(loop));
        ServiceId = serviceId;
        MethodId = methodId;
    }

    public CRpcLoop Loop { get; }

    public ushort ServiceId { get; }

    public ushort MethodId { get; }
}
```

Create `CRpc/Rpc/CRpc/Client/CRpcPushHandler.cs`:

```csharp
using CRpc.Async;

namespace CRpc.Rpc.CRpc.Client;

public delegate CRpcTask CRpcPushHandler(CRpcPushContext context, byte[] body);
```

- [ ] **Step 4: Add registration API to `IRpcClient`**

Update `CRpc/Rpc/IRPCClient.cs`:

```csharp
using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;

namespace CRpc.Rpc
{
    public interface IRpcClient
    {
        public CRpcTask<CRpcMessage> CallAsync(ushort serviceId, ushort methodId, byte[] body, int timeout);

        public void RegisterPushHandler(ushort serviceId, ushort methodId, CRpcPushHandler handler);
    }
}
```

- [ ] **Step 5: Implement push dispatch in `CRpcClient`**

In `CRpcClient`, add fields and callbacks:

```csharp
private readonly Dictionary<(ushort ServiceId, ushort MethodId), CRpcPushHandler> pushHandlers = new();

public Action<CRpcPushContext, Exception>? OnPushException { get; set; }

public Action<CRpcPushContext>? OnUnhandledPush { get; set; }
```

Add registration:

```csharp
public void RegisterPushHandler(ushort serviceId, ushort methodId, CRpcPushHandler handler)
{
    EnsureOwnerLoopThread();
    ArgumentNullException.ThrowIfNull(handler);
    pushHandlers[(serviceId, methodId)] = handler;
}
```

Change inbound handling so `CompleteReceiveResponse` routes by state:

```csharp
private void CompleteReceiveResponse(CRpcMessage message)
{
    var header = message.getHeader();
    if (header.hasState(CRpcMessageState.STATE_PUSH))
    {
        DispatchPush(message);
        return;
    }

    if (!header.hasState(CRpcMessageState.STATE_RESPONSE))
    {
        Console.WriteLine(
            $"CRpcClient ignored inbound message without response or push state: serviceId={message.getServiceId()}, methodId={message.getMethodId()}");
        return;
    }

    var reqSequence = message.getReqSequence();
    if (results.Remove(reqSequence, out var pendingCall))
    {
        pendingCall.TimeoutTimer?.Cancel();
        pendingCall.Source.TrySetResult(message);
    }
}
```

Add dispatch helpers:

```csharp
private void DispatchPush(CRpcMessage message)
{
    var serviceId = message.getServiceId();
    var methodId = message.getMethodId();
    var context = new CRpcPushContext(ownerLoop, serviceId, methodId);

    if (!pushHandlers.TryGetValue((serviceId, methodId), out var handler))
    {
        if (OnUnhandledPush is not null)
        {
            OnUnhandledPush(context);
        }
        else
        {
            Console.WriteLine($"CRpcClient unhandled push: serviceId={serviceId}, methodId={methodId}");
        }

        return;
    }

    try
    {
        var task = handler(context, message.getBody());
        ObservePushHandler(task, context);
    }
    catch (Exception exception)
    {
        ReportPushException(context, exception);
    }
}

private void ObservePushHandler(CRpcTask task, CRpcPushContext context)
{
    var awaiter = task.GetAwaiter();
    if (awaiter.IsCompleted)
    {
        CompletePushHandler(awaiter, context);
        return;
    }

    awaiter.OnCompleted(() => CompletePushHandler(awaiter, context));
}

private void CompletePushHandler(CRpcTask.Awaiter awaiter, CRpcPushContext context)
{
    try
    {
        awaiter.GetResult();
    }
    catch (Exception exception)
    {
        ReportPushException(context, exception);
    }
}

private void ReportPushException(CRpcPushContext context, Exception exception)
{
    if (OnPushException is not null)
    {
        OnPushException(context, exception);
        return;
    }

    Console.WriteLine(
        $"CRpcClient push handler exception: serviceId={context.ServiceId}, methodId={context.MethodId}, exception={exception}");
}
```

- [ ] **Step 6: Update test doubles implementing `IRpcClient`**

Search for `class .* : IRpcClient` and add:

```csharp
public void RegisterPushHandler(ushort serviceId, ushort methodId, CRpcPushHandler handler)
{
}
```

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcClientTests
```

Expected: `CRpcClientTests` pass.

---

## Task 3: Server Connection Runtime

**Files:**
- Create: `CRpc/Rpc/CRpc/Server/CRpcConnection.cs`
- Create: `CRpc/Rpc/CRpc/Server/CRpcConnectionRegistry.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcContext.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServer.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs`
- Create: `Tests/CRPC.Tests/CRpcConnectionTests.cs`
- Modify: `Tests/CRPC.Tests/CRpcServerHandlerTests.cs`

- [ ] **Step 1: Write focused connection tests**

Create `Tests/CRPC.Tests/CRpcConnectionTests.cs`:

```csharp
using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class CRpcConnectionTests : CrpcTestBase
{
    [Fact]
    public void SendPushAsyncWritesStatePushMessage()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var channel = new EmbeddedChannel();
        var connection = new CRpcConnection(loop, id: 42, channel);

        var task = connection.SendPushAsync(1000, 2, [1, 2, 3]);
        var awaiter = task.GetAwaiter();

        Assert.True(awaiter.IsCompleted);
        Assert.True(awaiter.GetResult());

        var outbound = Assert.IsType<CRpcMessage>(channel.ReadOutbound<object>());
        Assert.True(outbound.getHeader().hasState(CRpcMessageState.STATE_PUSH));
        Assert.True(outbound.getHeader().hasState(CRpcMessageState.NONE_ENCRYPT));
        Assert.False(outbound.getHeader().hasState(CRpcMessageState.STATE_RESPONSE));
        Assert.Equal(0, outbound.getReqSequence());
        Assert.Equal(1000, outbound.getServiceId());
        Assert.Equal(2, outbound.getMethodId());
        Assert.Equal([1, 2, 3], outbound.getBody());
    }

    [Fact]
    public void SendPushAsyncReturnsFalseWhenConnectionInactive()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var channel = new EmbeddedChannel();
        var connection = new CRpcConnection(loop, id: 42, channel);
        connection.MarkInactive();

        var awaiter = connection.SendPushAsync(1000, 2, Array.Empty<byte>()).GetAwaiter();

        Assert.True(awaiter.IsCompleted);
        Assert.False(awaiter.GetResult());
    }

    [Fact]
    public void SendPushAsyncThrowsWhenCalledOutsideOwnerLoop()
    {
        var loop = new CRpcLoop();
        var channel = new EmbeddedChannel();
        var connection = new CRpcConnection(loop, id: 42, channel);

        var exception = Assert.Throws<InvalidOperationException>(
            () => connection.SendPushAsync(1000, 2, Array.Empty<byte>()));

        Assert.Contains("owner CRpcLoop", exception.Message);
    }
}
```

- [ ] **Step 2: Run focused tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcConnectionTests
```

Expected: build fails because `CRpcConnection` does not exist.

- [ ] **Step 3: Implement `CRpcConnection`**

Create `CRpc/Rpc/CRpc/Server/CRpcConnection.cs`:

```csharp
using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcConnection
{
    private readonly CRpcLoop ownerLoop;
    private readonly IChannel channel;
    private bool isActive = true;

    internal CRpcConnection(CRpcLoop ownerLoop, long id, IChannel channel)
    {
        this.ownerLoop = ownerLoop ?? throw new ArgumentNullException(nameof(ownerLoop));
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        ConnectionId = id;
    }

    public long ConnectionId { get; }

    public bool IsActive => isActive && channel.Active;

    public CRpcTask<bool> SendPushAsync(ushort serviceId, ushort methodId, byte[] body)
    {
        EnsureOwnerLoopThread();
        ArgumentNullException.ThrowIfNull(body);

        if (!IsActive)
        {
            return CRpcTask.FromResult(false, ownerLoop);
        }

        var header = CRpcMessageHeader.valueOf(
            CRpcMessageState.STATE_PUSH,
            resultCode: 0,
            sn: 0,
            module: serviceId,
            command: methodId);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        var message = CRpcMessage.valueOf(header, body);

        try
        {
            var writeTask = channel.WriteAndFlushAsync(message);
            return CompleteWriteAsync(writeTask);
        }
        catch
        {
            return CRpcTask.FromResult(false, ownerLoop);
        }
    }

    internal void MarkInactive()
    {
        EnsureOwnerLoopThread();
        isActive = false;
    }

    private async CRpcTask<bool> CompleteWriteAsync(System.Threading.Tasks.Task writeTask)
    {
        try
        {
            await CRpcTask.FromTask(writeTask, ownerLoop);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureOwnerLoopThread()
    {
        var loop = CRpcLoop.Current
            ?? throw new InvalidOperationException("CRpcConnection operations must be called from a bound CRpcLoop thread.");
        if (!ReferenceEquals(ownerLoop, loop))
        {
            throw new InvalidOperationException("CRpcConnection operations must be called on the connection's owner CRpcLoop thread.");
        }
    }
}
```

- [ ] **Step 4: Implement connection registry**

Create `CRpc/Rpc/CRpc/Server/CRpcConnectionRegistry.cs`:

```csharp
using CRpc.Async;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcConnectionRegistry
{
    private readonly CRpcLoop ownerLoop;
    private readonly Dictionary<IChannel, CRpcConnection> byChannel = new();
    private readonly Dictionary<long, CRpcConnection> byId = new();
    private long nextConnectionId;

    internal CRpcConnectionRegistry(CRpcLoop ownerLoop)
    {
        this.ownerLoop = ownerLoop ?? throw new ArgumentNullException(nameof(ownerLoop));
    }

    public bool TryGet(long connectionId, out CRpcConnection connection)
    {
        EnsureOwnerLoopThread();
        return byId.TryGetValue(connectionId, out connection!);
    }

    public IReadOnlyList<CRpcConnection> Snapshot()
    {
        EnsureOwnerLoopThread();
        return byId.Values.ToArray();
    }

    internal CRpcConnection Register(IChannel channel)
    {
        EnsureOwnerLoopThread();
        var connection = new CRpcConnection(ownerLoop, ++nextConnectionId, channel);
        byChannel[channel] = connection;
        byId[connection.ConnectionId] = connection;
        return connection;
    }

    internal void Unregister(IChannel channel)
    {
        EnsureOwnerLoopThread();
        if (!byChannel.Remove(channel, out var connection))
        {
            return;
        }

        connection.MarkInactive();
        byId.Remove(connection.ConnectionId);
    }

    internal bool TryGetByChannel(IChannel channel, out CRpcConnection connection)
    {
        EnsureOwnerLoopThread();
        return byChannel.TryGetValue(channel, out connection!);
    }

    private void EnsureOwnerLoopThread()
    {
        var loop = CRpcLoop.Current
            ?? throw new InvalidOperationException("CRpcConnectionRegistry operations must be called from a bound CRpcLoop thread.");
        if (!ReferenceEquals(ownerLoop, loop))
        {
            throw new InvalidOperationException("CRpcConnectionRegistry operations must be called on the server owner CRpcLoop thread.");
        }
    }
}
```

- [ ] **Step 5: Add connection to context**

Replace `CRpc/Rpc/CRpc/Server/CRpcContext.cs`:

```csharp
namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcContext : IRpcContext
{
    public CRpcContext(CRpcConnection connection)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public CRpcConnection Connection { get; }
}
```

- [ ] **Step 6: Update server and handler**

In `CRpcServer`, add:

```csharp
public CRpcConnectionRegistry Connections { get; }
```

Initialize it in the constructor:

```csharp
Connections = new CRpcConnectionRegistry(loop);
```

In `CRpcServerHandler`, add `ChannelActive`:

```csharp
public override void ChannelActive(IChannelHandlerContext context)
{
    server.Loop.Post(() => server.Connections.Register(context.Channel));
    base.ChannelActive(context);
}
```

Update `ChannelInactive`:

```csharp
public override void ChannelInactive(IChannelHandlerContext context)
{
    server.Loop.Post(() => server.Connections.Unregister(context.Channel));
    Console.WriteLine($"CRpcServerHandler client disconnected: {context.Channel.RemoteAddress}");
    context.FireChannelInactive();
}
```

Update request processing to pass the connection:

```csharp
private void ProcessMessage(IRpcService rpcService, IChannelHandlerContext ctx, object msg)
{
    if (!server.Connections.TryGetByChannel(ctx.Channel, out var connection))
    {
        return;
    }

    var task = ProcessMessageAsync(rpcService, connection, ctx, msg);
    var awaiter = task.GetAwaiter();
    if (awaiter.IsCompleted)
    {
        CompleteProcessMessage(awaiter);
        return;
    }

    awaiter.OnCompleted(() => CompleteProcessMessage(awaiter));
}

private static async CRpcTask ProcessMessageAsync(
    IRpcService rpcService,
    CRpcConnection connection,
    IChannelHandlerContext ctx,
    object msg)
{
    var rpcContext = new CRpcContext(connection);
    var request = (CRpcMessage)msg;
    var (resultCode, bytes) = await RpcServiceInvoker.InvokeAsync(rpcService, rpcContext, request);
    var rsp = RpcServiceInvoker.BuildCrpcResponse(request, resultCode, bytes);
    ChannelWriteUtil.WriteAndFlushFireAndForget(ctx, rsp);
}
```

- [ ] **Step 7: Add server handler lifecycle tests**

Add to `CRpcServerHandlerTests`:

```csharp
[Fact]
public void ChannelActiveRegistersConnection()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var server = new CRpcServer(loop);
    var channel = CreateHandlerChannel(server);

    channel.Pipeline.FireChannelActive();
    loop.Tick();

    var connection = Assert.Single(server.Connections.Snapshot());
    Assert.True(connection.IsActive);
}

[Fact]
public void ChannelInactiveUnregistersConnection()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();
    var server = new CRpcServer(loop);
    var channel = CreateHandlerChannel(server);
    channel.Pipeline.FireChannelActive();
    loop.Tick();
    var connection = Assert.Single(server.Connections.Snapshot());

    channel.Pipeline.FireChannelInactive();
    loop.Tick();

    Assert.Empty(server.Connections.Snapshot());
    Assert.False(connection.IsActive);
}

[Fact]
public void ServiceReceivesCurrentConnectionInContext()
{
    var loop = new CRpcLoop();
    var service = new ContextRecordingService(NextServiceId());
    var server = new CRpcServer(loop);
    RegisterOnLoop(loop, service);
    var channel = CreateHandlerChannel(server);

    channel.Pipeline.FireChannelActive();
    loop.Tick();
    Assert.False(channel.WriteInbound(CreateRequest(service.GetServiceId())));
    loop.Tick();

    Assert.NotNull(service.Connection);
    Assert.Equal(1, service.Connection!.ConnectionId);
}
```

Add helper service:

```csharp
private sealed class ContextRecordingService : IRpcService
{
    private readonly ushort serviceId;

    public ContextRecordingService(ushort serviceId)
    {
        this.serviceId = serviceId;
    }

    public CRpcConnection? Connection { get; private set; }

    public ushort GetServiceId() => serviceId;

    public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
    {
        Connection = ((CRpcContext)context).Connection;
        return CRpcTask.FromResult((0, Array.Empty<byte>()), CRpcLoop.Current);
    }
}
```

- [ ] **Step 8: Run server runtime tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "CRPC.Tests.CRpcConnectionTests|CRPC.Tests.CRpcServerHandlerTests"
```

Expected: all focused server runtime tests pass.

---

## Task 4: Proto Option and Generator Shape

**Files:**
- Modify: `Tests/CRPC.Tests/CRPC.Tests.csproj`
- Modify: `CRpc/Rpc/CRpc/Protobuf/crpc-options.proto`
- Modify: `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcOptions.cs`
- Modify: `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcGen.cs`
- Create: `Tests/CRPC.Tests/CRpcGeneratorTests.cs`

- [ ] **Step 1: Reference the generator project from tests**

Add this project reference to `Tests/CRPC.Tests/CRPC.Tests.csproj`:

```xml
<ProjectReference Include="..\..\Tool\crpc-protobuf-plugin\CRpcProtobufPlugin\CRpcProtobufPlugin.csproj" />
```

- [ ] **Step 2: Add generator tests**

Create `Tests/CRPC.Tests/CRpcGeneratorTests.cs` with string-based tests that call `CRpcGen.Generate(...)` through a small `CodeGeneratorRequest`.

Use this helper skeleton:

```csharp
using Google.Protobuf.Compiler;
using Google.Protobuf.Reflection;
using CRpcProtobufPlugin;

namespace CRPC.Tests;

public class CRpcGeneratorTests : CrpcTestBase
{
    [Fact]
    public void GeneratesServiceBaseAndClientBaseNames()
    {
        var response = GenerateHelloWorld();

        var serverFile = Assert.Single(response.File, file => file.Name.EndsWith("Service.cs"));
        var clientFile = Assert.Single(response.File, file => file.Name.EndsWith("Client.cs"));
        Assert.Contains("public abstract class GreeterServiceBase", serverFile.Content);
        Assert.Contains("public abstract class GreeterClientBase", clientFile.Content);
        Assert.DoesNotContain("public abstract class GreeterBase", serverFile.Content);
        Assert.DoesNotContain("public sealed class GreeterClient", clientFile.Content);
    }

    [Fact]
    public void GeneratesPushHelperAndClientHandlerForServerPushMethod()
    {
        var response = GenerateHelloWorld(includePush: true);

        var serverFile = Assert.Single(response.File, file => file.Name.EndsWith("Service.cs"));
        var clientFile = Assert.Single(response.File, file => file.Name.EndsWith("Client.cs"));
        Assert.Contains("protected CRpcTask<bool> PushServerNoticeAsync", serverFile.Content);
        Assert.Contains("connection.SendPushAsync(1000, 2", serverFile.Content);
        Assert.Contains("protected virtual CRpcTask OnPushServerNoticeAsync", clientFile.Content);
        Assert.Contains("RegisterPushHandler(", clientFile.Content);
        Assert.DoesNotContain("ServerNoticeAsync(", clientFile.Content);
    }
}
```

- [ ] **Step 3: Run generator tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcGeneratorTests
```

Expected: build fails until generator test helpers and generator APIs are wired, then assertions fail on old generated names.

- [ ] **Step 4: Add `server_push` option**

Update `CRpc/Rpc/CRpc/Protobuf/crpc-options.proto`:

```proto
extend google.protobuf.MethodOptions {
  int32 method_id = 60002;
  bool server_push = 60003;
}
```

Update `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcOptions.cs` so it exposes:

```csharp
public static readonly pbr::Extension<global::Google.Protobuf.Reflection.MethodOptions, bool> ServerPush = ...
```

Update `CRpcOptions.cs` consistently with the existing generated extension style used for `ServiceId` and `MethodId`.

- [ ] **Step 5: Update server generator**

In `CRpcGen.GenerateServiceForServer`:

1. Generate `public abstract class {service.Name}ServiceBase : IRpcService, IRpcHttpJsonCodec`.
2. For ordinary methods, keep `OnMessageAsync`, HTTP parsers, private dispatch methods, and abstract service methods.
3. For push methods, skip `OnMessageAsync` dispatch and abstract method generation.
4. For push methods, emit:

```csharp
protected CRpcTask<bool> Push{method.Name}Async(CRpcConnection connection, {inType} message)
{
    ArgumentNullException.ThrowIfNull(connection);
    ArgumentNullException.ThrowIfNull(message);
    return connection.SendPushAsync({serviceId}, {methodId}, message.ToByteArray());
}
```

5. Validate push method output type:

```csharp
if (isServerPush && method.OutputType != ".google.protobuf.Empty")
{
    throw new Exception($"Service {service.Name}.{method.Name} server_push methods must return google.protobuf.Empty");
}
```

- [ ] **Step 6: Update client generator**

In `CRpcGen.GenerateServiceForClient`:

1. Generate `public abstract class {service.Name}ClientBase : ICRpcGeneratedClient`.
2. Emit `public IRpcClient __client = null!;`.
3. Emit `BindRpcClient(IRpcClient client)` that assigns `__client` and registers each push handler.
4. For ordinary methods, emit current `CallAsync`-based methods:

```csharp
public async CRpcTask<(int, {outType})> {method.Name}Async({inType} request, int timeOut = 5000)
{
    CRpcMessage message = await __client.CallAsync({serviceId}, {methodId}, request.ToByteArray(), timeOut);
    var result = message.getHeader().getResultCode();
    if (0 == result)
    {
        byte[] data = message.getBody();
        return (0, {outType}.Parser.ParseFrom(data));
    }

    return (-1, null);
}
```

5. For push methods, do not emit a public RPC call. Emit private adapter and protected virtual handler:

```csharp
private async CRpcTask __OnPush{method.Name}Async(CRpcPushContext context, byte[] body)
{
    await OnPush{method.Name}Async(context, {inType}.Parser.ParseFrom(body));
}

protected virtual CRpcTask OnPush{method.Name}Async(CRpcPushContext context, {inType} message)
{
    return CRpcTask.CompletedTask(context.Loop);
}
```

- [ ] **Step 7: Run generator tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcGeneratorTests
```

Expected: generator tests pass.

---

## Task 5: Example Migration

**Files:**
- Modify: `Example/HelloWorld/helloworld.proto`
- Modify: `Example/HelloWorld/Client/HelloworldClient.cs`
- Modify: `Example/HelloWorld/Server/HelloworldService.cs`
- Modify: `Example/HelloWorld/Server/HellowolrdServiceImpl.cs`
- Modify: `Example/HelloWorld/Client/Program.cs`
- Test: `Tests/CRPC.Tests/CRpcReferenceTests.cs`

- [ ] **Step 1: Add a push message to HelloWorld proto**

Update `Example/HelloWorld/helloworld-msg.proto` with a small push message:

```proto
message ServerNoticePush {
  string msg = 1;
}
```

Update `Example/HelloWorld/helloworld.proto`:

```proto
import "google/protobuf/empty.proto";

service Greeter {
    option (crpc.service_id) = 1000;
    rpc SayHello (HelloRequest) returns (HelloReply) { option (crpc.method_id) = 1; }
    rpc ServerNotice (ServerNoticePush) returns (google.protobuf.Empty) {
        option (crpc.method_id) = 2;
        option (crpc.server_push) = true;
    }
}
```

- [ ] **Step 2: Regenerate or manually align generated example files**

If the generator command is known and works, run it for HelloWorld. If not, manually update the generated example files to match the generator output while keeping the `// Code generated` header.

`Example/HelloWorld/Server/HelloworldService.cs` should expose `GreeterServiceBase`.

`Example/HelloWorld/Client/HelloworldClient.cs` should expose `GreeterClientBase : ICRpcGeneratedClient`.

- [ ] **Step 3: Update server implementation**

Change `Example/HelloWorld/Server/HellowolrdServiceImpl.cs` to inherit the new base:

```csharp
public class HelloworldServiceImpl : GreeterServiceBase
{
    protected override CRpcTask<(int, HelloReply)> SayHelloAsync(
        CRpcContext context,
        HelloRequest request)
    {
        _ = PushServerNoticeAsync(
            context.Connection,
            new ServerNoticePush { Msg = $"server saw: {request.Msg}" });

        return CRpcTask.FromResult(
            (0, new HelloReply { Msg = $"Hello {request.Msg}" }),
            CRpcLoop.Current);
    }
}
```

- [ ] **Step 4: Add user-owned concrete client**

In `Example/HelloWorld/Client/HelloworldClient.cs` or a new `Example/HelloWorld/Client/GreeterClient.cs`, define the concrete client:

```csharp
public sealed class GreeterClient : GreeterClientBase
{
    protected override CRpcTask OnPushServerNoticeAsync(
        CRpcPushContext context,
        ServerNoticePush message)
    {
        Console.WriteLine($"server push: {message.Msg}");
        return CRpcTask.CompletedTask(context.Loop);
    }
}
```

If the generated file owns `GreeterClientBase`, prefer creating a separate non-generated file for the concrete `GreeterClient`.

- [ ] **Step 5: Keep client program call site unchanged**

Verify `Example/HelloWorld/Client/Program.cs` still uses:

```csharp
var reference = await CRpcReference
    .For<GreeterClient>()
    .Url("crpc://127.0.0.1:7999")
    .ConnectAsync(loop);
```

Do not add manual push registration in `Program.cs`.

- [ ] **Step 6: Build examples**

Run:

```bash
dotnet build orient-dotnet.sln
```

Expected: solution builds successfully.

---

## Task 6: End-to-End Push Test

**Files:**
- Create: `Tests/CRPC.Tests/CRpcServerPushIntegrationTests.cs`

- [ ] **Step 1: Write an integration test with an embedded server channel**

Create `Tests/CRPC.Tests/CRpcServerPushIntegrationTests.cs`:

```csharp
using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class CRpcServerPushIntegrationTests : CrpcTestBase
{
    [Fact]
    public void ServiceCanPushToCurrentConnectionWithoutClientAck()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var service = new PushOnRequestService();
        var server = new CRpcServer(loop);
        loop.RegisterService(service);
        var channel = new EmbeddedChannel(
            new CRpcMessageEncoder(
                CRpcServerOptions.DefaultHashLength,
                CRpcServerOptions.DefaultCompressThreshold),
            new CRpcServerHandler(server));

        channel.Pipeline.FireChannelActive();
        loop.Tick();

        Assert.False(channel.WriteInbound(CreateRequest(PushOnRequestService.ServiceId)));
        loop.Tick();

        var response = Assert.IsType<CRpcMessage>(channel.ReadOutbound<object>());
        var push = Assert.IsType<CRpcMessage>(channel.ReadOutbound<object>());

        Assert.True(response.getHeader().hasState(CRpcMessageState.STATE_RESPONSE));
        Assert.True(push.getHeader().hasState(CRpcMessageState.STATE_PUSH));
        Assert.False(push.getHeader().hasState(CRpcMessageState.STATE_RESPONSE));
        Assert.Equal(PushOnRequestService.ServiceId, push.getServiceId());
        Assert.Equal(PushOnRequestService.PushMethodId, push.getMethodId());
        Assert.Equal([7, 8, 9], push.getBody());
    }

    private static CRpcMessage CreateRequest(ushort serviceId)
    {
        var header = CRpcMessageHeader.valueOf(
            CRpcMessageState.STATE_NONE,
            resultCode: 0,
            sn: 1,
            module: serviceId,
            command: 1);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        return CRpcMessage.valueOf(header, Array.Empty<byte>());
    }

    private sealed class PushOnRequestService : IRpcService
    {
        public const ushort ServiceId = 1234;
        public const ushort PushMethodId = 2;

        public ushort GetServiceId() => ServiceId;

        public async CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            var rpcContext = (CRpcContext)context;
            await rpcContext.Connection.SendPushAsync(ServiceId, PushMethodId, [7, 8, 9]);
            return (0, Array.Empty<byte>());
        }
    }
}
```

- [ ] **Step 2: Run the integration test**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcServerPushIntegrationTests
```

Expected: test passes and shows response plus push are both written without any client acknowledgement path.

---

## Task 7: Full Verification and Cleanup

**Files:**
- Review all files changed in Tasks 1-6.

- [ ] **Step 1: Run focused runtime tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "CRPC.Tests.CRpcClientTests|CRPC.Tests.CRpcConnectionTests|CRPC.Tests.CRpcServerHandlerTests|CRPC.Tests.CRpcReferenceTests|CRPC.Tests.CRpcServerPushIntegrationTests"
```

Expected: all selected tests pass.

- [ ] **Step 2: Run generator tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter CRPC.Tests.CRpcGeneratorTests
```

Expected: generator tests pass.

- [ ] **Step 3: Run full test project**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: all non-live tests pass. If live LordUnion tests are skipped by default, keep them skipped.

- [ ] **Step 4: Build full solution**

Run:

```bash
dotnet build orient-dotnet.sln
```

Expected: build succeeds with no new warnings from touched files.

- [ ] **Step 5: Inspect generated API migration**

Search:

```bash
rg "GreeterBase|public sealed class GreeterClient|XxxBase|__BindPushHandlers" Example CRpc Tool Tests
```

Expected:

- No generated server class named `GreeterBase`.
- No generated sealed `GreeterClient` in generated output.
- No `__BindPushHandlers`; generated clients use `BindRpcClient`.
- User-owned concrete `GreeterClient : GreeterClientBase` exists.

---

## Self-Review

Spec coverage:

- `STATE_PUSH` routing is covered by Tasks 2 and 6.
- Fire-and-forget server push is covered by Tasks 3 and 6.
- `CRpcConnection`, registry, and `CRpcContext.Connection` are covered by Task 3.
- `XxxServiceBase` and `XxxClientBase` generation is covered by Task 4.
- HelloWorld migration and unchanged `CRpcReference.For<GreeterClient>()` usage are covered by Task 5.
- Error behavior for unknown push, handler exception, inactive connection, and channel write failure is covered by Tasks 2, 3, and 7.

Placeholder scan:

- This plan has no TBD/TODO placeholders.
- Generator tests reference the existing `CRpcProtobufPlugin` namespace and add the required project reference explicitly.

Type consistency:

- Push runtime uses `CRpcPushContext`, `CRpcPushHandler`, `ICRpcGeneratedClient`, `CRpcConnection`, and `CRpcConnectionRegistry` consistently.
- Generated client binding uses `BindRpcClient`, not `__BindPushHandlers`.
- Generated server naming uses `XxxServiceBase`; generated client naming uses `XxxClientBase`.
