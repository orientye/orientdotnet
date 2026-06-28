# Multi-Endpoint CRpc + HttpServer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run one `CRpcLoop` with **CRpc on 7999** and **HTTP JSON on 8080**, sharing a single service registry and the same `OnMessageAsync` business path.

**Architecture:** Move `ServiceRegistry` to `CRpcLoop`. Split `CRpcServer.RunAsync` into `StartAsync`/`StopAsync` plus `CRpcLoopHost`. Extract `RpcServiceInvoker` for loop-thread dispatch. Add `HttpServer` (DotNetty HTTP) that converts JSON via codegen-provided `IRpcHttpJsonCodec` parsers, then reuses the invoker.

**Tech Stack:** C# / .NET 8, `CRpcTask`, DotNetty 0.7.6 (+ `DotNetty.Codecs.Http`), Google.Protobuf JSON, xUnit.

**Spec:** `docs/superpowers/specs/2026-05-19-multi-endpoint-crpc-http-design.md`

---

## File Structure

| File | Action |
|------|--------|
| `CRpc/Async/CRpcLoop.cs` | Add service registry |
| `CRpc/Rpc/IRpcHttpJsonCodec.cs` | Create — HTTP parser metadata |
| `CRpc/Rpc/CRpc/Server/CRpcServerOptions.cs` | Create |
| `CRpc/Rpc/CRpc/Server/HttpServerOptions.cs` | Create |
| `CRpc/Rpc/CRpc/Server/CRpcLoopHost.cs` | Create (move logic from `CRpcServerLoop`) |
| `CRpc/Rpc/CRpc/Server/RpcServiceInvoker.cs` | Create |
| `CRpc/Rpc/CRpc/Server/HttpServer.cs` | Create |
| `CRpc/Rpc/CRpc/Server/HttpServerHandler.cs` | Create |
| `CRpc/Rpc/CRpc/Server/CRpcServer.cs` | `StartAsync`/`StopAsync`, registry → loop |
| `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs` | Use `RpcServiceInvoker` |
| `CRpc/Rpc/CRpc/Server/CRpcServerLoop.cs` | Obsolete forwarder or delete after migrate |
| `CRpc/CRPC.csproj` | Add `DotNetty.Codecs.Http` |
| `Tool/crpc-protobuf-plugin/.../CRpcGen.cs` | Generate `IRpcHttpJsonCodec` |
| `Example/HelloWorld/Server/*Service*.cs` | Regenerate / update |
| `Tests/CRPC.Tests/CRpcLoopRegistryTests.cs` | Create |
| `Tests/CRPC.Tests/RpcServiceInvokerTests.cs` | Create |
| `Tests/CRPC.Tests/HttpServerHandlerTests.cs` | Create |
| `Tests/CRPC.Tests/CRpcServerHandlerTests.cs` | Register via `loop.RegisterService` |
| `Example/HelloWorld/Server/Program.cs` | 7999 + 8080 startup |
| `Doc/gateway.md` | HTTP contract |
| `Doc/architecture.md` | `HttpServer` naming note |

---

### Task 1: Service registry on `CRpcLoop`

**Files:**
- Modify: `CRpc/Async/CRpcLoop.cs`
- Create: `Tests/CRPC.Tests/CRpcLoopRegistryTests.cs`

- [ ] **Step 1: Write failing registry tests**

```csharp
using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Server;

namespace CRPC.Tests;

public class CRpcLoopRegistryTests
{
    [Fact]
    public void RegisterServiceRequiresLoopThread()
    {
        var loop = new CRpcLoop();
        var service = new RecordingService(1000);
        Assert.Throws<InvalidOperationException>(() => loop.RegisterService(service));
    }

    [Fact]
    public void RegisterAndTryGetServiceOnLoopThread()
    {
        var loop = new CRpcLoop();
        var service = new RecordingService(1000);
        loop.Post(() => loop.RegisterService(service));
        loop.Tick();
        loop.Post(() =>
        {
            Assert.True(loop.TryGetService(1000, out var found));
            Assert.Same(service, found);
        });
        loop.Tick();
    }

    private sealed class RecordingService : IRpcService
    {
        public RecordingService(ushort serviceId) => ServiceId = serviceId;
        public ushort ServiceId { get; }
        public ushort GetServiceId() => ServiceId;
        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req) =>
            CRpcTask.FromResult((0, Array.Empty<byte>()));
    }
}
```

Add `using CRpc.Async;` and `IRpcMessage` import via `CRpc.Rpc.CRpc.Codec` if needed for `IRpcMessage` — use same `RecordingService` pattern as `CRpcServerHandlerTests` (copy minimal version or make internal helper).

- [ ] **Step 2: Run tests — expect compile fail**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcLoopRegistryTests" --no-restore
```

Expected: `RegisterService` / `TryGetService` not found on `CRpcLoop`.

- [ ] **Step 3: Implement registry on `CRpcLoop`**

Add to `CRpcLoop`:

```csharp
private readonly Dictionary<ushort, IRpcService> registeredServices = new();

public void RegisterService(IRpcService service)
{
    EnsureLoopThread();
    ArgumentNullException.ThrowIfNull(service);
    registeredServices[service.GetServiceId()] = service;
}

public bool TryGetService(ushort serviceId, [MaybeNullWhen(false)] out IRpcService service)
{
    EnsureLoopThread();
    return registeredServices.TryGetValue(serviceId, out service);
}

public void UnregisterService(IRpcService service)
{
    EnsureLoopThread();
    ArgumentNullException.ThrowIfNull(service);
    var id = service.GetServiceId();
    if (registeredServices.TryGetValue(id, out var registered) && ReferenceEquals(registered, service))
        registeredServices.Remove(id);
}
```

Add `using System.Diagnostics.CodeAnalysis;`, `using CRpc.Rpc;`, and extend `EnsureLoopThread` message to cover registry ops (reuse existing private method).

- [ ] **Step 4: Run tests — PASS**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcLoopRegistryTests"
```

---

### Task 2: `CRpcServer` delegates registry to loop

**Files:**
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServer.cs`
- Modify: `Tests/CRPC.Tests/CRpcServerHandlerTests.cs`

- [ ] **Step 1: Change `RegisterOnLoop` helper in tests**

Replace `server.RegisterService(service)` with `loop.RegisterService(service)` in `RegisterOnLoop` and any direct `server.RegisterService` calls.

- [ ] **Step 2: Forward `CRpcServer` registry methods to `Loop`**

```csharp
public void RegisterService(IRpcService service)
{
    EnsureLoopThread();
    Loop.RegisterService(service);
}

public bool TryGetRegisteredService(ushort serviceId, [MaybeNullWhen(false)] out IRpcService service)
{
    EnsureLoopThread();
    return Loop.TryGetService(serviceId, out service);
}

public void UnregisterService(IRpcService service) => Loop.UnregisterService(service);

internal void ClearRegisteredServices()
{
    EnsureLoopThread();
    // remove all — only used on shutdown; loop can add internal clear or iterate Unregister
}
```

Remove `registeredServices` field and dictionary from `CRpcServer` after migration. For `ClearRegisteredServices`, add `internal void ClearRegisteredServices()` on `CRpcLoop` that clears dictionary on loop thread (used by server shutdown).

- [ ] **Step 3: Run existing server handler tests**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcServerHandlerTests"
```

Expected: PASS.

---

### Task 3: `IRpcHttpJsonCodec` + codegen parsers

**Files:**
- Create: `CRpc/Rpc/IRpcHttpJsonCodec.cs`
- Modify: `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcGen.cs`
- Modify: `Example/HelloWorld/Server/HelloworldService.cs` (regenerated)

- [ ] **Step 1: Add interface**

```csharp
using Google.Protobuf;

namespace CRpc.Rpc;

public interface IRpcHttpJsonCodec
{
    bool TryGetHttpMethodParsers(
        ushort methodId,
        out MessageParser requestParser,
        out MessageParser responseParser);
}
```

- [ ] **Step 2: Extend `GenerateServiceForServer`**

After `GetServiceId()` method generation, add interface to base class declaration:

```csharp
sb.AppendLine($"public abstract class {service.Name}Base : IRpcService, IRpcHttpJsonCodec");
```

After `OnMessageAsync` method, emit:

```csharp
public bool TryGetHttpMethodParsers(ushort methodId, out MessageParser requestParser, out MessageParser responseParser)
{
    requestParser = null!;
    responseParser = null!;
    if (methodId == {methodId}) { requestParser = {inType}.Parser; responseParser = {outType}.Parser; return true; }
    ...
    return false;
}
```

Add `using Google.Protobuf;` is already present. Add `using CRpc.Rpc;` for interface in generated file (already has `using CRpc.Rpc`).

- [ ] **Step 3: Regenerate HelloWorld service**

Run project-specific protoc/plugin command used by repo (check `Example/HelloWorld` README or existing script). Verify `GreeterBase` implements `TryGetHttpMethodParsers` for `SayHello`.

- [ ] **Step 4: Build**

```bash
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj
```

Expected: PASS.

---

### Task 4: `RpcServiceInvoker`

**Files:**
- Create: `CRpc/Rpc/CRpc/Server/RpcServiceInvoker.cs`
- Create: `Tests/CRPC.Tests/RpcServiceInvokerTests.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs`

- [ ] **Step 1: Failing test — invoker returns code and body on loop**

Use `EmbeddedChannel` + `RecordingService` that returns `(0, new byte[] { 1, 2, 3 })` from `OnMessageAsync`. Call invoker on loop thread after `loop.Tick()` from Posted work, or call invoker directly from loop.Post in test.

Minimal test:

```csharp
[Fact]
public void InvokeAsyncReturnsServiceResult()
{
    var loop = new CRpcLoop();
    var service = new ByteReturnService(1000);
    loop.Post(() => loop.RegisterService(service));
    loop.Tick();

    (int code, byte[] body)? result = null;
    var req = CreateRequest(1000, methodId: 1);
    loop.Post(async () => result = await RpcServiceInvoker.InvokeAsync(service, new CRpcContext(), req));
    loop.Tick();
    // drain async continuation — may need multiple Tick or use sync-completed service

    Assert.NotNull(result);
    Assert.Equal(0, result.Value.code);
    Assert.Equal(new byte[] { 9 }, result.Value.body);
}
```

Prefer `RecordingService`-style synchronous `CRpcTask.FromResult` for deterministic single `Tick`.

- [ ] **Step 2: Implement `RpcServiceInvoker`**

```csharp
namespace CRpc.Rpc.CRpc.Server;

internal static class RpcServiceInvoker
{
    public static async CRpcTask<(int code, byte[] body)> InvokeAsync(
        IRpcService service,
        CRpcContext context,
        CRpcMessage request)
    {
        var (code, body) = await service.OnMessageAsync(context, request);
        return (code, body);
    }

    public static CRpcMessage BuildCrpcResponse(CRpcMessage request, int code, byte[] body)
    {
        return request.createResponse(code, body);
    }
}
```

- [ ] **Step 3: Refactor `CRpcServerHandler.ProcessMessageAsync`**

Replace inline `OnMessageAsync` + `createResponse` with:

```csharp
var (resultCode, bytes) = await RpcServiceInvoker.InvokeAsync(rpcService, rpcContext, request);
var rsp = RpcServiceInvoker.BuildCrpcResponse(request, resultCode, bytes);
rsp.encryptAndCompress(512, true, true);
// ... existing frame write
```

- [ ] **Step 4: Run tests**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~RpcServiceInvokerTests|FullyQualifiedName~CRpcServerHandlerTests"
```

---

### Task 5: `CRpcServerOptions`, `StartAsync`, `StopAsync`, `CRpcLoopHost`

**Files:**
- Create: `CRpc/Rpc/CRpc/Server/CRpcServerOptions.cs`
- Create: `CRpc/Rpc/CRpc/Server/CRpcLoopHost.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServer.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServerLoop.cs`

- [ ] **Step 1: Add options + host**

`CRpcServerOptions`:

```csharp
using System.Net;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcServerOptions
{
    public IPAddress Address { get; init; } = IPAddress.Any;
    public int Port { get; init; } = 7999;
    public int MaxFrameLength { get; init; } = 32768;
    public int HashLength { get; init; } = 16;
}
```

`CRpcLoopHost`:

```csharp
public static class CRpcLoopHost
{
    public static void RunUntilCancelled(CRpcLoop loop, CancellationToken cancellationToken, int sleepMilliseconds = 1)
    {
        CRpcServerLoop.RunUntilCancelled(loop, cancellationToken, sleepMilliseconds);
    }
}
```

(Initial forward keeps diff small; optionally inline body into `CRpcLoopHost` and obsolete `CRpcServerLoop`.)

- [ ] **Step 2: Refactor `CRpcServer` constructor and fields**

```csharp
private readonly CRpcServerOptions options;

public CRpcServer(CRpcLoop loop, CRpcServerOptions? options = null)
{
    ArgumentNullException.ThrowIfNull(loop);
    Loop = loop;
    this.options = options ?? new CRpcServerOptions();
}
```

Remove `registeredServices` dictionary (done in Task 2).

- [ ] **Step 3: Extract `StartAsync` / `StopAsync`**

Move bind logic from `RunAsync(IPAddress, int, ...)` into:

```csharp
public async Task StartAsync(CancellationToken cancellationToken = default)
{
    if (bootstrapChannel is not null)
        throw new InvalidOperationException("CRpcServer is already started.");
    // create groups, bootstrap, bind options.Address / options.Port
    // store channel + groups; do NOT call RunUntilCancelled here
}

public async Task StopAsync()
{
    runCancellation?.Cancel();
    if (bootstrapChannel is not null)
        await bootstrapChannel.CloseAsync();
    // shutdown groups, null fields
}
```

`RunAsync` becomes:

```csharp
public async Task RunAsync(IPAddress address, int port, bool registerConsoleCancelHandler = true)
{
    options = new CRpcServerOptions { Address = address, Port = port }; // or pass via ctor before Run
    using var cts = new CancellationTokenSource();
    await StartAsync(cts.Token);
    // console cancel → Close()
    CRpcLoopHost.RunUntilCancelled(Loop, cts.Token);
    await StopAsync();
}
```

Prefer: require `CRpcServerOptions` on ctor; `RunAsync()` uses ctor options.

- [ ] **Step 4: Build + run handler tests**

```bash
dotnet build CRpc/CRPC.csproj
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcServerHandlerTests"
```

---

### Task 6: DotNetty HTTP package

**Files:**
- Modify: `CRpc/CRPC.csproj`

- [ ] **Step 1: Add package reference**

```xml
<PackageReference Include="DotNetty.Codecs.Http" Version="0.7.6" />
```

- [ ] **Step 2: Restore and build**

```bash
dotnet build CRpc/CRPC.csproj
```

Expected: PASS.

---

### Task 7: `HttpServer` + `HttpServerHandler`

**Files:**
- Create: `CRpc/Rpc/CRpc/Server/HttpServerOptions.cs`
- Create: `CRpc/Rpc/CRpc/Server/HttpServer.cs`
- Create: `CRpc/Rpc/CRpc/Server/HttpServerHandler.cs`

- [ ] **Step 1: `HttpServerOptions`**

```csharp
public sealed class HttpServerOptions
{
    public IPAddress Address { get; init; } = IPAddress.Any;
    public int Port { get; init; } = 8080;
    public int MaxContentLength { get; init; } = 1024 * 1024;
}
```

- [ ] **Step 2: `HttpServer` skeleton**

Mirror `CRpcServer.StartAsync`/`StopAsync` with `ServerBootstrap`, pipeline:

```csharp
pipeline.AddLast(new HttpServerCodec());
pipeline.AddLast(new HttpObjectAggregator(options.MaxContentLength));
pipeline.AddLast(new HttpServerHandler(loop));
```

- [ ] **Step 3: `HttpServerHandler` core logic**

`ChannelRead0(IFullHttpRequest)`:

1. Validate `POST` and path `/(\d+)/(\d+)` → `serviceId`, `methodId`.
2. Require `Content-Type` contains `application/json` (case-insensitive).
3. `loop.Post(() => ProcessOnLoop(ctx, request, serviceId, methodId, jsonUtf8))`.

`ProcessOnLoop`:

1. `loop.TryGetService` — fail → write 404 JSON/text.
2. `service is IRpcHttpJsonCodec codec` && `codec.TryGetHttpMethodParsers` — else 404.
3. Parse JSON → protobuf bytes; build `CRpcMessage` via `CRpcMessageHeader.valueOf(STATE_NONE, 0, sn, serviceId, methodId)` + `NONE_ENCRYPT`.
4. `await RpcServiceInvoker.InvokeAsync`.
5. Parse response bytes with `responseParser`; build `{"code":N,"body":{...}}` using `JsonFormatter`.
6. `WriteAndFlushAsync` `DefaultFullHttpResponse(HttpResponseStatus.OK)` with `Content-Type: application/json`.

Use `Interlocked.Increment` for request `sn` on HTTP path.

Schedule async `CRpcTask` completion same as `CRpcServerHandler` (`GetAwaiter().OnCompleted`).

**Important:** Capture `IChannelHandlerContext` and write response from loop thread only if consistent with CRpc handler (current CRpcServerHandler writes from loop after await — follow same pattern).

- [ ] **Step 4: Build**

```bash
dotnet build CRpc/CRPC.csproj
```

---

### Task 8: `HttpServerHandler` tests

**Files:**
- Create: `Tests/CRPC.Tests/HttpServerHandlerTests.cs`

- [ ] **Step 1: Test JSON round-trip with generated codec**

Use `GreeterBase` implementation or test double implementing `IRpcService` + `IRpcHttpJsonCodec`:

```csharp
[Fact]
public void PostJsonInvokesServiceAndReturnsEnvelope()
{
    var loop = new CRpcLoop();
    var service = new TestGreeterService(); // returns (0, HelloReply bytes)
    loop.Post(() => loop.RegisterService(service));
    loop.Tick();

    var channel = new EmbeddedChannel(
        new HttpServerCodec(),
        new HttpObjectAggregator(65536),
        new HttpServerHandler(loop));

    var request = new DefaultFullHttpRequest(
        HttpVersion.Http11, HttpMethod.Post, "/1000/1");
    request.Headers.Set(HttpHeaderNames.ContentType, "application/json");
    request.Content.WriteBytes(Encoding.UTF8.GetBytes("{\"name\":\"test\"}"));

    Assert.True(channel.WriteInbound(request));
    loop.Tick(); // may need extra ticks for async

    var response = channel.ReadOutbound<IFullHttpResponse>();
    Assert.NotNull(response);
    Assert.Equal(200, response.Status.Code);
    var json = response.Content.ToString(Encoding.UTF8);
    Assert.Contains("\"code\":0", json);
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~HttpServerHandlerTests"
```

- [ ] **Step 3: Test 415 for wrong content type**

POST with `text/plain` → 415.

---

### Task 9: HelloWorld multi-endpoint example

**Files:**
- Modify: `Example/HelloWorld/Server/Program.cs`

- [ ] **Step 1: Update startup**

```csharp
var loop = new CRpcLoop();
loop.Post(() => loop.RegisterService(new HelloworldServiceImpl()));
loop.Tick(); // register before bind if StartAsync requires loop thread — OR register inside Post before host runs

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var crpc = new CRpcServer(loop, new CRpcServerOptions { Port = 7999 });
var http = new HttpServer(loop, new HttpServerOptions { Port = 8080 });

await crpc.StartAsync(cts.Token);
await http.StartAsync(cts.Token);
Console.WriteLine("CRpc 7999, HTTP 8080");
CRpcLoopHost.RunUntilCancelled(loop, cts.Token);
await http.StopAsync();
await crpc.StopAsync();
```

Ensure `RegisterService` runs on loop thread: register inside first `Post` before `RunUntilCancelled`, or call `BindToCurrentThread` on main then register (match existing patterns).

- [ ] **Step 2: Manual smoke**

Terminal 1:

```bash
dotnet run --project Example/HelloWorld/Server/HelloWorldServer.csproj
```

Terminal 2:

```bash
curl -s -X POST http://127.0.0.1:8080/1000/1 -H "Content-Type: application/json" -d "{\"name\":\"world\"}"
```

Expected: `{"code":0,"body":{...}}` with greeting message.

---

### Task 10: Documentation

**Files:**
- Modify: `Doc/gateway.md`
- Modify: `Doc/architecture.md` (short §4.2 naming note)

- [ ] **Step 1: Write `Doc/gateway.md`**

Document:

- Ports 7999 / 8080
- `POST /{serviceId}/{methodId}`
- JSON request/response envelope
- `IRpcHttpJsonCodec` requirement
- IO → `loop.Post` invariant

- [ ] **Step 2: Add architecture-draft note**

In §4.2 diagram, replace `HttpGatewayServer` with `HttpServer` and note spec path.

---

## Plan Self-Review

| Spec requirement | Task |
|------------------|------|
| Registry on loop | 1, 2 |
| 7999 CRpc + 8080 HTTP | 5, 6, 7, 9 |
| `HttpServer` naming | 7 |
| JSON `POST /{serviceId}/{methodId}` | 7, 8 |
| `IRpcHttpJsonCodec` codegen | 3 |
| Shared invoker | 4 |
| `CRpcLoopHost` | 5 |
| No octet-stream / Kestrel | — (non-goals) |
| Docs | 10 |

**Placeholder scan:** None.

**Note:** `RunAsync` on `CRpcServer` uses `Task` only at IO bind boundary — acceptable interop per project rules. Business logic remains `CRpcTask`.

---

## Verification (full suite)

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj
```
