# CRpc Gateway Phase 1 Implementation Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden `Example/GateWay` for production Phase 1: always respond to client RPCs, relay backend push to the correct inbound connection, per-inbound backend links, and clean connection lifecycle.

**Architecture:** Extract testable `GateWay.Core` library. `GateWaySessionTable` maps each inbound `CRpcConnection` to a dedicated outbound `CRpcClient`. `GateWayPushRelay` registers push handlers on outbound clients and forwards to the bound inbound connection. `GateWayServerHandler` writes error responses on all failure paths.

**Tech Stack:** C# / .NET 8, DotNetty `EmbeddedChannel`, `CRpcLoop` / `CRpcTask`, xUnit.

**Spec reference:** `docs/superpowers/specs/2026-06-01-crpc-gateway-design.md`

**Repository rule:** Do not create commits unless the user explicitly requests them.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `Example/GateWay/GateWay.Core.csproj` | New class library — gateway logic (testable) |
| `Example/GateWay/GateWayServer.csproj` | Thin host exe; references `GateWay.Core` |
| `Example/GateWay/GateWayOptions.cs` | Backend host/port, default timeout, fallback service id |
| `Example/GateWay/GateWayResponseUtil.cs` | `WriteErrorResponse(ctx, request, resultCode)` helper |
| `Example/GateWay/GateWayBackendLink.cs` | Pairs inbound `CRpcConnection` + outbound `CRpcClient` |
| `Example/GateWay/GateWaySessionTable.cs` | Loop-owned map `ConnectionId → GateWayBackendLink`; create/remove |
| `Example/GateWay/GateWayPushRelay.cs` | Register outbound push handlers; forward to inbound |
| `Example/GateWay/GateWayRouter.cs` | Resolve backend address; delegate session table for client per inbound |
| `Example/GateWay/GateWayServiceImpl.cs` | Forward via session link; use `GateWayOptions.DefaultTimeoutMs` |
| `Example/GateWay/GateWayServerHandler.cs` | Fallback routing + error responses + session cleanup on inactive |
| `Example/GateWay/Program.cs` | Wire options, session table, push relay, shutdown cleanup |
| `Example/GateWay/Client/Program.cs` | Unchanged URL; verify push log lines after Phase 1 |
| `Tests/CRPC.Tests/GateWay/GateWayServerHandlerTests.cs` | Error-response and fallback tests |
| `Tests/CRPC.Tests/GateWay/GateWaySessionTableTests.cs` | Session create/remove tests |
| `Tests/CRPC.Tests/CRPC.Tests.csproj` | Add `ProjectReference` to `GateWay.Core` |
| `orient-dotnet.sln` | Add `GateWay.Core` project |

---

## Task 1: Extract `GateWay.Core` Class Library

**Files:**
- Create: `Example/GateWay/GateWay.Core.csproj`
- Modify: `Example/GateWay/GateWayServer.csproj`
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
    <ProjectReference Include="..\..\CRPC\CRPC.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Move gateway `.cs` files into Core**

Move (keep namespace `GateWay`):

- `GateWayRouter.cs`
- `GateWayServiceImpl.cs`
- `GateWayServerHandler.cs`

`GateWayServer.csproj` keeps only `Program.cs` and references `GateWay.Core.csproj`. Keep existing `Client\` exclusion rules.

- [ ] **Step 3: Add `GateWay.Core` to solution**

```bash
dotnet sln orient-dotnet.sln add Example/GateWay/GateWay.Core.csproj
```

- [ ] **Step 4: Verify build**

```bash
dotnet build Example/GateWay/GateWayServer.csproj
```

Expected: BUILD SUCCESS.

---

## Task 2: Always Respond on Handler Failure Paths (P0)

**Files:**
- Create: `Example/GateWay/GateWayResponseUtil.cs`
- Modify: `Example/GateWay/GateWayServerHandler.cs`
- Create: `Tests/CRPC.Tests/GateWay/GateWayServerHandlerTests.cs`
- Modify: `Tests/CRPC.Tests/CRPC.Tests.csproj`

- [ ] **Step 1: Add project reference in tests**

```xml
<ProjectReference Include="..\..\Example\GateWay\GateWay.Core.csproj" />
```

- [ ] **Step 2: Write failing handler tests**

Create `Tests/CRPC.Tests/GateWay/GateWayServerHandlerTests.cs` with:

1. `UnknownServiceWithFallbackWritesErrorResponse` — register fallback service that is never invoked if test uses unregistered id; instead verify fallback runs. Simpler test: **no service registered at all** for target id, fallback returns `(-1, [])`, client receives `STATE_RESPONSE` with `resultCode=-1`.
2. `MissingInboundConnectionWritesErrorResponse` — fire `ChannelRead` before `ChannelActive`/`Register`; expect outbound `STATE_RESPONSE` frame (not empty pipeline).
3. `NoFallbackRegisteredWritesErrorResponse` — only register unrelated service; send `serviceId=1000`; expect response with `resultCode=-1`.

Use `EmbeddedChannel` + `CRpcMessageEncoder` pattern from `CRpcServerHandlerTests`. Handler factory:

```csharp
new GateWayServerHandler(server, fallbackServiceId: 0)
```

- [ ] **Step 3: Run tests and verify failure**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~GateWayServerHandlerTests"
```

Expected: FAIL — no response on missing-connection / no-fallback paths.

- [ ] **Step 4: Implement `GateWayResponseUtil`**

```csharp
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

In `ChannelRead` posted lambda:

```csharp
else
{
    GateWayResponseUtil.WriteErrorResponse(ctx, message);
}
```

In `ProcessMessage`:

```csharp
if (!server.Connections.TryGetByChannel(ctx.Channel, out var connection))
{
    GateWayResponseUtil.WriteErrorResponse(ctx, (CRpcMessage)msg);
    return;
}
```

- [ ] **Step 6: Re-run tests**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~GateWayServerHandlerTests"
```

Expected: PASS.

---

## Task 3: `GateWayOptions` and Configurable Timeout

**Files:**
- Create: `Example/GateWay/GateWayOptions.cs`
- Modify: `Example/GateWay/GateWayServiceImpl.cs`
- Modify: `Example/GateWay/Program.cs`

- [ ] **Step 1: Add options type**

```csharp
public sealed class GateWayOptions
{
    public string BackendHost { get; init; } = "127.0.0.1";
    public int BackendPort { get; init; } = 7999;
    public int DefaultTimeoutMs { get; init; } = 5000;
    public ushort FallbackServiceId { get; init; } = 0;
}
```

- [ ] **Step 2: Inject options into `GateWayServiceImpl`**

Replace hard-coded `5000` with `options.DefaultTimeoutMs`.

- [ ] **Step 3: Wire in `Program.cs`**

```csharp
var options = new GateWayOptions { BackendPort = 7999 };
// pass to router/service/handler
```

- [ ] **Step 4: Build**

```bash
dotnet build Example/GateWay/GateWayServer.csproj
```

Expected: BUILD SUCCESS.

---

## Task 4: Per-Inbound Backend Link (`GateWaySessionTable`)

**Files:**
- Create: `Example/GateWay/GateWayBackendLink.cs`
- Create: `Example/GateWay/GateWaySessionTable.cs`
- Modify: `Example/GateWay/GateWayRouter.cs`
- Create: `Tests/CRPC.Tests/GateWay/GateWaySessionTableTests.cs`

- [ ] **Step 1: Write failing session table tests**

- `GetOrCreateLinkReturnsSameClientForSameConnection` — mock or stub: second call same `ConnectionId` returns same link instance.
- `RemoveLinkDropsEntry` — after remove, next get creates new entry (use counter or new client id).

- [ ] **Step 2: Implement `GateWayBackendLink`**

```csharp
public sealed class GateWayBackendLink : IAsyncDisposable
{
    public CRpcConnection Inbound { get; }
    public CRpcClient BackendClient { get; }
    // ctor(loop, inbound, backendClient)
    public async ValueTask DisposeAsync() => await BackendClient.CloseAsync();
}
```

- [ ] **Step 3: Implement `GateWaySessionTable`**

Loop-thread-only dictionary `long ConnectionId → GateWayBackendLink`.

```csharp
public async CRpcTask<GateWayBackendLink> GetOrCreateAsync(
    CRpcConnection inbound,
    GateWayOptions options,
    CRpcLoop loop)
{
    // if exists return
    // else new CRpcClient(loop), ConnectAsync(host, port), store, return
}
```

`Remove(long connectionId)` — dispose link and remove.

- [ ] **Step 4: Update `GateWayRouter`**

Hold `GateWaySessionTable` + `GateWayOptions`. Replace `Register(ushort, IRpcClient)` with:

```csharp
public CRpcTask<IRpcClient> GetBackendClientAsync(CRpcConnection inbound, ushort serviceId)
```

For Phase 1 only `serviceId=1000` is supported; unknown service returns null (forwarder returns -1).

- [ ] **Step 5: Update `GateWayServiceImpl`**

```csharp
var connection = ((CRpcContext)context).Connection;
var backend = await router.GetBackendClientAsync(connection, targetServiceId);
```

- [ ] **Step 6: Run session tests**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~GateWaySessionTableTests"
```

Expected: PASS.

---

## Task 5: Push Relay (P1)

**Files:**
- Create: `Example/GateWay/GateWayPushRelay.cs`
- Modify: `Example/GateWay/GateWaySessionTable.cs` (hook after link create)
- Modify: `Example/GateWay/Program.cs`

- [ ] **Step 1: Implement `GateWayPushRelay`**

```csharp
public sealed class GateWayPushRelay
{
    public void Attach(GateWayBackendLink link)
    {
        link.BackendClient.RegisterPushHandler(serviceId, methodId, (ctx, body) =>
            ForwardPushAsync(link, serviceId, methodId, body));
    }

    private static async CRpcTask ForwardPushAsync(
        GateWayBackendLink link, ushort serviceId, ushort methodId, byte[] body)
    {
        await link.Inbound.SendPushAsync(serviceId, methodId, body);
    }
}
```

Phase 1: register handlers for HelloWorld push `(1000, 2)` when creating a link. Future: table-driven from config.

- [ ] **Step 2: Call `Attach` in `GetOrCreateAsync` after connect**

- [ ] **Step 3: Manual verification**

Terminal 1:

```bash
dotnet run --project Example/HelloWorld/Server/HelloWorldServer.csproj
```

Terminal 2:

```bash
dotnet run --project Example/GateWay/GateWayServer.csproj
```

Terminal 3:

```bash
dotnet run --project Example/GateWay/Client/GateWayClient.csproj
```

Expected: each `SayHello` prints **`server push: server saw: ...`** on client console (from `GreeterClient.OnPushServerNoticeAsync`).

- [ ] **Step 4: Optional unit test**

`GateWayPushRelayTests` with recording `CRpcConnection` mock or test double verifying `SendPushAsync` called when outbound push handler fires.

---

## Task 6: Inbound Disconnect and Gateway Shutdown Cleanup

**Files:**
- Modify: `Example/GateWay/GateWayServerHandler.cs`
- Modify: `Example/GateWay/GateWayRouter.cs` or `GateWaySessionTable.cs`
- Modify: `Example/GateWay/Program.cs`

- [ ] **Step 1: Pass `GateWaySessionTable` into handler**

Add constructor parameter or access via router held by server (prefer explicit `GateWaySessionTable sessions` on handler).

- [ ] **Step 2: `ChannelInactive` cleanup**

```csharp
server.Loop.Post(() =>
{
    server.Connections.Unregister(context.Channel);
    if (server.Connections.TryGetByChannel(...) // already unregistered
    sessions.Remove(connectionId);
});
```

Unregister first, capture `connectionId` before unregister, then `sessions.Remove(connectionId)`.

- [ ] **Step 3: Shutdown in `Program.cs` `finally`**

```csharp
await sessions.DisposeAllAsync(); // dispose every backend link
await server.StopAsync();
```

- [ ] **Step 4: Manual test**

Start Gateway + HelloWorld + Client; Ctrl+C Gateway; restart Gateway; Client reconnects and calls succeed.

---

## Task 7: Backend Reconnect (Single Retry)

**Files:**
- Modify: `Example/GateWay/GateWayServiceImpl.cs`
- Modify: `Example/GateWay/GateWaySessionTable.cs`

- [ ] **Step 1: Write test or manual checklist**

Stop HelloWorld while Gateway running; start HelloWorld; next client RPC should succeed without restarting Gateway.

- [ ] **Step 2: On `CallAsync` catch `InvalidOperationException` ("not connected")**

```csharp
await link.ReconnectAsync(options);
response = await link.BackendClient.CallAsync(...); // one retry only
```

Implement `GateWayBackendLink.ReconnectAsync` → `ConnectAsync(options.BackendHost, options.BackendPort)`.

- [ ] **Step 3: If retry fails, return `(-1, [])`**

Client receives error response, not hang.

---

## Task 8: Remove Shared Global `backendClient` from `Program.cs`

**Files:**
- Modify: `Example/GateWay/Program.cs`

- [ ] **Step 1: Delete startup `CRpcClient` + `router.Register(1000, backendClient)`**

Session table creates clients lazily per inbound connection.

- [ ] **Step 2: Register only `GateWayServiceImpl` + start server**

- [ ] **Step 3: Full manual regression**

| Step | Expected |
|------|----------|
| 3-process happy path | 5× hello + 5× push lines |
| Gateway only, no backend | Client gets `-1` quickly, no hang |
| Two GateWay clients concurrently | Both get correct push on their own calls |

---

## Task 9: Final Verification

- [ ] **Step 1: Run all CRPC tests**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: all PASS (including new GateWay tests).

- [ ] **Step 2: Build solution**

```bash
dotnet build orient-dotnet.sln
```

Expected: BUILD SUCCESS.

- [ ] **Step 3: Update spec status if needed**

Mark Phase 1 items done in `docs/superpowers/specs/2026-06-01-crpc-gateway-design.md` when implementation merges.

---

## Deferred to Phase 2 (do not implement here)

- `BackendPool` with multiple endpoints per `serviceId`
- Weighted random / round-robin pick
- `userId` / `SessionRegistry` sticky routing
- External route config file
- Merging `GateWayServerHandler` into `CRpcServerHandler`
- Client timeout plumbed from request ext header

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
