# CRpc Gateway Phase 1 Implementation Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden `Example/GateWay` for production Phase 1: always respond to client RPCs, relay backend push to the correct inbound connection, per-inbound backend links, and clean connection lifecycle.

**Architecture:** Extract testable `GateWay.Core` library under `Example/GateWay/GateWay.Core/`. `GateWaySessionTable` maps each inbound `CRpcConnection` to a dedicated outbound `CRpcClient`. `GateWayPushRelay` registers push handlers on outbound clients and forwards to the bound inbound connection. `GateWayServerHandler` writes error responses on all failure paths.

**Tech Stack:** C# / .NET 8, DotNetty `EmbeddedChannel`, `CRpcLoop` / `CRpcTask`, xUnit, `CrpcTestBase`.

**Spec reference:** `docs/superpowers/specs/2026-06-01-crpc-gateway-design.md`

**Repository rule:** Do not create commits unless the user explicitly requests them.

**Recommended task order:** 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 (verification)

---

## Directory Layout (required — do not put two `.csproj` in one folder)

```text
Example/GateWay/
  GateWay.Core/
    GateWay.Core.csproj
    GateWayRouter.cs
    GateWayServiceImpl.cs
    GateWayServerHandler.cs
    GateWayOptions.cs
    GateWayResponseUtil.cs
    GateWayBackendLink.cs
    GateWaySessionTable.cs
    GateWayPushRelay.cs
    IBackendClientFactory.cs          # test seam
  GateWayServer/
    GateWayServer.csproj
    Program.cs
  Client/
    GateWayClient.csproj
    Program.cs
```

Move existing `Example/GateWay/GateWayServer.csproj` + `Program.cs` into `GateWayServer/`. Update `orient-dotnet.sln` paths accordingly.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `Example/GateWay/GateWay.Core/GateWay.Core.csproj` | Class library — all gateway logic |
| `Example/GateWay/GateWayServer/GateWayServer.csproj` | Thin host exe; references `GateWay.Core` |
| `Example/GateWay/GateWay.Core/GateWayOptions.cs` | Backend host/port, timeout, fallback id, routed `serviceId` set |
| `Example/GateWay/GateWay.Core/GateWayResponseUtil.cs` | `WriteErrorResponse(ctx, request, resultCode)` |
| `Example/GateWay/GateWay.Core/GateWayBackendLink.cs` | Inbound `CRpcConnection` + outbound `CRpcClient`; `ReconnectAsync` |
| `Example/GateWay/GateWay.Core/IBackendClientFactory.cs` | `Create(CRpcLoop)` for tests |
| `Example/GateWay/GateWay.Core/GateWaySessionTable.cs` | `ConnectionId → GateWayBackendLink`; `Remove`; `DisposeAllAsync` |
| `Example/GateWay/GateWay.Core/GateWayPushRelay.cs` | Outbound push → inbound `SendPushAsync` |
| `Example/GateWay/GateWay.Core/GateWayRouter.cs` | Route table + session table delegation |
| `Example/GateWay/GateWay.Core/GateWayServiceImpl.cs` | Forward via session link; timeout + retry |
| `Example/GateWay/GateWay.Core/GateWayServerHandler.cs` | Fallback routing, error responses, session cleanup |
| `Example/GateWay/GateWayServer/Program.cs` | Wire options, relay, shutdown |
| `Tests/CRPC.Tests/GateWay/GateWayServerHandlerTests.cs` | Handler error-response tests |
| `Tests/CRPC.Tests/GateWay/GateWaySessionTableTests.cs` | Session table tests with factory stub |
| `Tests/CRPC.Tests/CRPC.Tests.csproj` | Reference `GateWay.Core` |

---

## Task 1: Extract `GateWay.Core` Class Library

**Files:**
- Create: `Example/GateWay/GateWay.Core/GateWay.Core.csproj`
- Create: `Example/GateWay/GateWayServer/GateWayServer.csproj` (move from parent)
- Move: `Program.cs` → `GateWayServer/`
- Move: `GateWayRouter.cs`, `GateWayServiceImpl.cs`, `GateWayServerHandler.cs` → `GateWay.Core/`
- Modify: `orient-dotnet.sln`

- [ ] **Step 1: Create `GateWay.Core.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>GateWay</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\CRPC\CRPC.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `GateWayServer/GateWayServer.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\GateWay.Core\GateWay.Core.csproj" />
  </ItemGroup>
</Project>
```

Remove the old `Example/GateWay/GateWayServer.csproj` at the parent level.

- [ ] **Step 3: Update solution project paths**

```bash
dotnet sln orient-dotnet.sln add Example/GateWay/GateWay.Core/GateWay.Core.csproj
# Remove old GateWayServer entry if path changed; re-add:
dotnet sln orient-dotnet.sln add Example/GateWay/GateWayServer/GateWayServer.csproj
```

- [ ] **Step 4: Verify build**

```bash
dotnet build Example/GateWay/GateWayServer/GateWayServer.csproj
```

Expected: BUILD SUCCESS.

---

## Task 2: Always Respond on Handler Failure Paths (P0)

**Files:**
- Create: `Example/GateWay/GateWay.Core/GateWayResponseUtil.cs`
- Modify: `Example/GateWay/GateWay.Core/GateWayServerHandler.cs`
- Create: `Tests/CRPC.Tests/GateWay/GateWayServerHandlerTests.cs`
- Modify: `Tests/CRPC.Tests/CRPC.Tests.csproj`

- [ ] **Step 1: Add project reference in tests**

```xml
<ProjectReference Include="..\..\Example\GateWay\GateWay.Core\GateWay.Core.csproj" />
```

- [ ] **Step 2: Write failing handler tests**

Create `GateWayServerHandlerTests` extending **`CrpcTestBase`** (same as `CRpcServerHandlerTests`).

Tests:

1. **`NoFallbackRegisteredWritesErrorResponse`** — loop has no services registered; send `serviceId=1000`; read outbound frame; decode with `CRpcMessageDecoder`; assert `STATE_RESPONSE` and `resultCode=-1`.
2. **`MissingInboundConnectionWritesErrorResponse`** — fire `ChannelRead` before `ChannelActive`; assert outbound response frame exists with `resultCode=-1`.
3. **`FallbackRoutesToRegisteredForwarder`** — register stub `IRpcService` with `GetServiceId() => 0` returning `(-1, [])`; send `serviceId=1000`; assert stub was invoked (proves fallback path, not silent drop).

Helper pattern:

```csharp
private static CRpcMessage ReadResponse(EmbeddedChannel channel)
{
    var frame = channel.ReadOutbound<IByteBuffer>();
    Assert.NotNull(frame);
    return CRpcMessage.valueOf(frame);
}
```

Handler under test:

```csharp
new GateWayServerHandler(server, fallbackServiceId: 0)
```

- [ ] **Step 3: Run tests and verify failure**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~GateWayServerHandlerTests"
```

Expected: FAIL on tests 1–2 before implementation.

- [ ] **Step 4: Implement `GateWayResponseUtil`**

```csharp
using CRpc.Rpc.CRpc;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels;

internal static class GateWayResponseUtil
{
    public static void WriteResponse(IChannelHandlerContext ctx, CRpcMessage request, int resultCode, byte[] body)
    {
        var response = request.createResponse(resultCode, body);
        ChannelWriteUtil.WriteAndFlushFireAndForget(ctx, response);
    }

    public static void WriteErrorResponse(IChannelHandlerContext ctx, CRpcMessage request, int resultCode = -1)
    {
        WriteResponse(ctx, request, resultCode, Array.Empty<byte>());
    }
}
```

- [ ] **Step 5: Update `GateWayServerHandler`**

In `ChannelRead` posted lambda `else` branch:

```csharp
GateWayResponseUtil.WriteErrorResponse(ctx, message);
```

In `ProcessMessage` when connection missing:

```csharp
GateWayResponseUtil.WriteErrorResponse(ctx, (CRpcMessage)msg);
return;
```

- [ ] **Step 6: Re-run tests — expect PASS**

---

## Task 3: `GateWayOptions` and Configurable Timeout

**Files:**
- Create: `Example/GateWay/GateWay.Core/GateWayOptions.cs`
- Modify: `GateWayServiceImpl.cs`, `GateWayServer/Program.cs`

- [ ] **Step 1: Add options type**

```csharp
public sealed class GateWayOptions
{
    public string BackendHost { get; init; } = "127.0.0.1";
    public int BackendPort { get; init; } = 7999;
    public int DefaultTimeoutMs { get; init; } = 5000;
    public ushort FallbackServiceId { get; init; } = 0;
    /// <summary>Phase 1: serviceIds that may be forwarded (e.g. HelloWorld Greeter 1000).</summary>
    public HashSet<ushort> RoutedServiceIds { get; init; } = [1000];
}
```

- [ ] **Step 2: Inject options into `GateWayServiceImpl` and `GateWayRouter`**

Replace hard-coded `5000` with `options.DefaultTimeoutMs`.

- [ ] **Step 3: Wire `Program.cs` — single source for fallback id**

```csharp
var options = new GateWayOptions();
var server = new CRpcServer(loop, new CRpcServerOptions
{
    Port = 7000,
    HandlerFactory = srv => new GateWayServerHandler(srv, options.FallbackServiceId),
});
loop.RegisterService(new GateWayServiceImpl(router, options));
```

Do **not** hardcode `fallbackServiceId: 0` in multiple places.

- [ ] **Step 4: Build — expect SUCCESS**

---

## Task 4: Per-Inbound Backend Link + Remove Global `backendClient`

**Files:**
- Create: `GateWayBackendLink.cs`, `IBackendClientFactory.cs`, `GateWaySessionTable.cs`
- Modify: `GateWayRouter.cs`, `GateWayServiceImpl.cs`, `GateWayServer/Program.cs`
- Create: `Tests/CRPC.Tests/GateWay/GateWaySessionTableTests.cs`

- [ ] **Step 1: Add `IBackendClientFactory`**

```csharp
public interface IBackendClientFactory
{
    CRpcClient Create(CRpcLoop loop);
}
```

Default production factory: `() => new CRpcClient(loop)`. Tests inject a factory that returns pre-built or counting clients.

- [ ] **Step 2: Write failing session table tests** (`CrpcTestBase`)

- `GetOrCreateLinkReturnsSameClientForSameConnection` — use stub factory; same `ConnectionId` → same `GateWayBackendLink` instance.
- `RemoveLinkDropsEntry` — after `Remove`, next `GetOrCreate` creates a new link (factory create count increments).
- `ConnectFailureReturnsNull` — factory/client that fails connect → `GetOrCreateAsync` returns null; forwarder returns `-1`.

- [ ] **Step 3: Implement `GateWayBackendLink`**

```csharp
public sealed class GateWayBackendLink : IAsyncDisposable
{
    public CRpcConnection Inbound { get; }
    public CRpcClient BackendClient { get; }
    private readonly GateWayOptions options;

    public async CRpcTask ReconnectAsync()
    {
        if (BackendClient is { } client)
        {
            await client.CloseAsync();
        }
        await BackendClient.ConnectAsync(options.BackendHost, options.BackendPort);
    }

    public async ValueTask DisposeAsync()
    {
        await BackendClient.CloseAsync();
        await BackendClient.ShutdownIoAsync();
    }
}
```

- [ ] **Step 4: Implement `GateWaySessionTable`**

Loop-thread-only `Dictionary<long, GateWayBackendLink>`.

```csharp
public async CRpcTask<GateWayBackendLink?> GetOrCreateAsync(
    CRpcConnection inbound, GateWayOptions options, CRpcLoop loop)
{
    if (links.TryGetValue(inbound.ConnectionId, out var existing))
        return existing;

    var client = backendClientFactory.Create(loop);
    try
    {
        await client.ConnectAsync(options.BackendHost, options.BackendPort);
    }
    catch
    {
        return null;
    }

    var link = new GateWayBackendLink(inbound, client, options);
    links[inbound.ConnectionId] = link;
    return link;
}

public async CRpcTask RemoveAsync(long connectionId)
{
    if (links.Remove(connectionId, out var link))
        await link.DisposeAsync();
}

public async CRpcTask DisposeAllAsync()
{
    foreach (var link in links.Values)
        await link.DisposeAsync();
    links.Clear();
}
```

- [ ] **Step 5: Update `GateWayRouter`**

```csharp
public async CRpcTask<IRpcClient?> GetBackendClientAsync(CRpcConnection inbound, ushort serviceId)
{
    if (!options.RoutedServiceIds.Contains(serviceId))
        return null;
    var link = await sessionTable.GetOrCreateAsync(inbound, options, loop);
    return link?.BackendClient;
}
```

- [ ] **Step 6: Update `GateWayServiceImpl`**

```csharp
var connection = ((CRpcContext)context).Connection;
var backend = await router.GetBackendClientAsync(connection, targetServiceId);
if (backend is null)
    return (-1, Array.Empty<byte>());
```

- [ ] **Step 7: Remove global `backendClient` from `Program.cs`**

Delete startup `CRpcClient`, `ConnectAsync`, and `router.Register(1000, ...)`. Session table creates clients lazily per inbound connection.

- [ ] **Step 8: Run session tests — expect PASS**

---

## Task 5: Push Relay (P1)

**Files:**
- Create: `GateWayPushRelay.cs`
- Modify: `GateWaySessionTable.cs`, `GateWayServer/Program.cs`

**Constraint:** `RegisterPushHandler` and `SendPushAsync` must run on the owner `CRpcLoop` thread. Call `Attach` from `GetOrCreateAsync` (already on loop thread via `OnMessageAsync` path).

- [ ] **Step 1: Implement `GateWayPushRelay`**

Phase 1 hard-codes HelloWorld `ServerNotice`: `(serviceId: 1000, methodId: 2)`. Mark with comment: HelloWorld demo only; Phase 2+ table-driven.

```csharp
public void Attach(GateWayBackendLink link)
{
    link.BackendClient.RegisterPushHandler(1000, 2, (ctx, body) =>
        link.Inbound.SendPushAsync(1000, 2, body));
}
```

- [ ] **Step 2: Inject relay into session table; call `Attach` after successful connect**

- [ ] **Step 3: Manual verification** (3 terminals — HelloWorld, GateWayServer, GateWayClient)

Expected: each `SayHello` prints **`server push: server saw: ...`**.

- [ ] **Step 4: (Optional)** `GateWayPushRelayTests` with inbound test double recording `SendPushAsync`.

---

## Task 6: Inbound Disconnect and Gateway Shutdown Cleanup

**Files:**
- Modify: `GateWayServerHandler.cs`, `GateWayServer/Program.cs`

- [ ] **Step 1: Add `GateWaySessionTable sessions` to handler constructor**

- [ ] **Step 2: Fix `ChannelInactive` cleanup order**

Capture id **before** unregister:

```csharp
public override void ChannelInactive(IChannelHandlerContext context)
{
    server.Loop.Post(() =>
    {
        long? connectionId = null;
        if (server.Connections.TryGetByChannel(context.Channel, out var connection))
            connectionId = connection.ConnectionId;

        server.Connections.Unregister(context.Channel);

        if (connectionId is not null)
            _ = sessions.RemoveAsync(connectionId.Value);
    });
    context.FireChannelInactive();
}
```

- [ ] **Step 3: Shutdown in `Program.cs` `finally`**

```csharp
CRpcLoopRunner.RunUntilComplete(loop, async () =>
{
    await sessions.DisposeAllAsync();
    await server.StopAsync();
});
```

- [ ] **Step 4: Manual test** — Ctrl+C Gateway; restart; client reconnects successfully.

---

## Task 7: Backend Reconnect (Single Retry)

**Files:**
- Modify: `GateWayServiceImpl.cs`

- [ ] **Step 1: Wrap forward in try/retry**

```csharp
try
{
    response = await backend.CallAsync(..., options.DefaultTimeoutMs);
}
catch (Exception ex) when (IsReconnectable(ex))
{
    var link = await router.GetLinkAsync(connection); // or hold link from first call
    if (link is null) return (-1, Array.Empty<byte>());
    await link.ReconnectAsync();
    try
    {
        response = await backend.CallAsync(..., options.DefaultTimeoutMs);
    }
    catch
    {
        return (-1, Array.Empty<byte>());
    }
}
```

`IsReconnectable`: `InvalidOperationException` with "not connected"; optionally `SocketException`. **Do not** retry on `CRpcTask` timeout/cancel — return `-1` immediately after first failure.

- [ ] **Step 2: Manual test** — stop HelloWorld while Gateway up; restart HelloWorld; next RPC succeeds without Gateway restart.

---

## Task 8: Final Verification

- [ ] **Step 1: Run all CRPC tests**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

- [ ] **Step 2: Build solution**

```bash
dotnet build orient-dotnet.sln
```

- [ ] **Step 3: Full manual regression**

| Step | Expected |
|------|----------|
| 3-process happy path | 5× hello + 5× push lines |
| Gateway only, no backend | Client gets `-1` quickly, no hang |
| Restart HelloWorld | Gateway recovers without restart |
| Two concurrent GateWay clients | Each gets its own push, no cross-talk |

- [ ] **Step 4: Update spec status** when implementation lands.

---

## Deferred to Phase 2 (do not implement here)

- `BackendPool` with multiple endpoints per `serviceId`
- Weighted random / round-robin pick (stateless traffic only)
- `SessionRegistry` / `userId` sticky routing (Phase 3)
- External route config file
- Merging `GateWayServerHandler` into `CRpcServerHandler`
- Client timeout plumbed from request ext header
- Table-driven push handler registration

---

## Manual Test Checklist (copy for PR / review)

```
[ ] HelloWorld :7999 + Gateway :7000 + GateWayClient — RPC OK
[ ] GateWayClient prints server push after each SayHello
[ ] Gateway without backend — client error, no timeout hang
[ ] Restart HelloWorld — Gateway recovers without restart
[ ] Two concurrent GateWay clients — isolated push per client
[ ] dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj — green
```
