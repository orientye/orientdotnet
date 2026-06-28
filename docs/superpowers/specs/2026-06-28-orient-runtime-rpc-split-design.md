# Orient.Runtime / Orient.Rpc Split Design

**Status:** Draft (pending review)  
**Date:** 2026-06-28  
**Implementation:** Completed by `docs/superpowers/plans/2026-06-28-a2-runtime-rpc-split.md`.  
**Prerequisite:** `docs/superpowers/specs/2026-06-28-a2-rpc-service-registry-design.md` (A2 must be implemented as part of the same delivery)  
**Related:** `Doc/architecture.md`, `docs/superpowers/specs/2026-06-27-crpc-server-lifecycle-design.md`, `docs/superpowers/specs/2026-06-20-crpc-http-separation-design.md`

**Implementation:** One combined plan covering **A2 + this split + type rename** (see Open Items).

---

## Goal

Split the monolithic `CRPC` assembly into two packages so non-RPC consumers (future `Orient.DataManager`, HTTP-only hosts) depend only on the execution runtime:

```text
Orient.Runtime   ← loop, task, timer, runner, loop host (BCL only)
Orient.Rpc       ← RPC wire protocol, server, client, registry, transport, nacos integration
```

After split:

```text
Orient.DataManager  →  Orient.Runtime only
HelloWorld / GateWay / CRPC.Tests  →  Orient.Rpc (transitively Orient.Runtime)
HTTP-only Center (future)          →  Orient.Runtime + Orient.DataManager (no Orient.Rpc required)
```

Rename execution types to match the package brand:

| Before | After |
| --- | --- |
| `CRpcLoop` | `OrientLoop` |
| `CRpcTask` | `OrientTask` |
| `CRpcTask<T>` | `OrientTask<T>` |
| `CRpcTaskCompletionSource<T>` | `OrientTaskCompletionSource<T>` |
| `CRpcLoopRunner` | `OrientLoopRunner` |
| `CRpcLoopHost` / `CRpcServerLoop` / `CRpcClientLoop` | `OrientLoopHost` (single implementation) |
| `CRpcLoopOptions` | `OrientLoopOptions` |
| `CRpcLoopTimer` | `OrientLoopTimer` |
| `ICRpcLoopTimerScheduler` | `IOrientLoopTimerScheduler` |
| `CRpcAsyncMethodBuilder*` | `OrientAsyncMethodBuilder*` |

**Keep `CRpc*` prefix** on wire-protocol and endpoint types in `Orient.Rpc` (e.g. `CRpcServer`, `CRpcClient`, `CRpcMessage`). CRpc = protocol product name; Orient = execution runtime brand.

---

## Non-Goals

- `Orient.DataManager` implementation (separate future spec)
- DB Proxy / storage drivers
- Shared `RpcServiceRegistry` injection across servers (see A2 spec)
- Splitting Nacos into a third package (stays in `Orient.Rpc` for v1)
- Splitting or repackaging the protobuf plugin tool (`Tool/crpc-protobuf-plugin`) — generated code **must** be updated to reference `Orient.Runtime` / `Orient.Rpc`
- Kestrel / official HTTP package
- Multi-loop routing or cross-loop `InvokeAsync` (architecture-draft future item)

---

## Background

Today a single `CRPC.csproj` references DotNetty, Protobuf, and nacos-sdk-csharp. All consumers pull the full dependency graph even if they only need `CRpcLoop` and `CRpcTask`.

The monolith also embeds RPC service registration on `CRpcLoop` (removed by A2). After A2, the loop is logically pure; the csproj split makes that dependency boundary enforceable at build time.

`architecture.md` lists **independent `ServiceRegistry` type** (A2) and notes Runtime as a direction. This spec completes the physical split.

---

## Target Dependency Graph

```text
                    ┌─────────────────┐
                    │  Applications   │
                    │ HelloWorld,     │
                    │ GateWay, Tests  │
                    └────────┬────────┘
                             │
                    ┌────────▼────────┐
                    │   Orient.Rpc    │
                    │ DotNetty, Proto │
                    │ nacos-sdk       │
                    └────────┬────────┘
                             │
                    ┌────────▼────────┐
                    │ Orient.Runtime  │
                    │ (BCL only)      │
                    └─────────────────┘

Future:
  Orient.DataManager ──► Orient.Runtime
  Example/Center     ──► Orient.Runtime + Orient.DataManager
```

**Rules:**

1. `Orient.Runtime` MUST NOT reference `Orient.Rpc`.
2. `Orient.Rpc` MUST reference `Orient.Runtime`.
3. No circular references.
4. Application HTTP layers may reference both; binary CRpc apps reference `Orient.Rpc` only (Runtime is transitive).

---

## Project Layout

```text
orientdotnet/
├── Orient.Runtime/
│   ├── Orient.Runtime.csproj
│   ├── Loop/           OrientLoop, OrientLoopOptions, OrientLoopHost, …
│   ├── Task/           OrientTask, OrientTaskCompletionSource, async builders
│   └── Timer/          OrientLoopTimer, MinHeapTimerScheduler, …
├── Orient.Rpc/
│   ├── Orient.Rpc.csproj
│   ├── Interfaces/   IRpcService, IRpcContext, IRpcMessage
│   ├── Server/         CRpcServer, RpcServiceRegistry, CRpcServerHandler, …
│   ├── Client/         CRpcClient, references, push, …
│   ├── Codec/          CRpcMessage, encoder/decoder, …
│   ├── Protocol/       CRpcStatusCode
│   ├── Transport/      TcpChannelHost, LoopInboundHandler, …
│   ├── Registry/       NacosRegistry (optional integration)
│   ├── ConfigCenter/   NacosConfig
│   └── Mgr/            NacosMgr
├── Tests/
│   ├── Orient.Runtime.Tests/   (optional phase 2)
│   └── Orient.Rpc.Tests/       (rename/migrate from CRPC.Tests, or keep name v1)
├── Example/ …
└── (remove) CRPC/ or CRpc/ monolith csproj
```

**Solution (`orient-dotnet.sln`):** Replace `CRPC` project entry with `Orient.Runtime` and `Orient.Rpc`.

---

## File Migration

### Orient.Runtime (from `CRpc/Async/`)

| Source | Notes |
| --- | --- |
| `CRpcLoop.cs` | Rename → `OrientLoop.cs`; **no registry** (A2) |
| `CRpcLoopOptions.cs` | → `OrientLoopOptions.cs` |
| `CRpcLoopRunner.cs` | → `OrientLoopRunner.cs` |
| `CRpcLoopTimer.cs` | → `OrientLoopTimer.cs` |
| `ICRpcLoopTimerScheduler.cs` | → `IOrientLoopTimerScheduler.cs` |
| `MinHeapTimerScheduler.cs` | unchanged logic |
| `CRpcTask.cs` | → `OrientTask.cs` |
| `CRpcTask.Generic.cs` | → `OrientTask.Generic.cs` |
| `CRpcTaskStatus.cs` | → `OrientTaskStatus.cs` |
| `CRpcTaskCompletionSource.cs` | → `OrientTaskCompletionSource.cs` |
| `CRpcAsyncMethodBuilder.cs` | → `OrientAsyncMethodBuilder.cs` |
| `CRpcAsyncMethodBuilder.Generic.cs` | → `OrientAsyncMethodBuilder.Generic.cs` |

**New / merged in Runtime:**

| File | Action |
| --- | --- |
| `OrientLoopHost.cs` | **New** — merge `CRpcServerLoop` + `CRpcClientLoop` (identical `Tick` + `WaitForWorkOrTimer` loops) |

**Orient.Runtime.csproj:**

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <AssemblyName>Orient.Runtime</AssemblyName>
  <RootNamespace>Orient.Runtime</RootNamespace>
</PropertyGroup>
<!-- No PackageReference beyond implicit BCL -->
```

**InternalsVisibleTo:** `Orient.Rpc`, test assemblies as needed.

### Orient.Rpc (everything else from monolith)

| Area | Files |
| --- | --- |
| **Interfaces** | `IRPCService.cs`, `IRPCContext.cs`, `IRpcMessage.cs` |
| **Server** | `CRpcServer.cs`, `CRpcServerHandler.cs`, `CRpcServerOptions.cs`, `CRpcConnection*.cs`, `CRpcContext.cs`, `RpcServiceInvoker.cs`, `CRpcServerPipelineFactory.cs`, `CRpcServerReadIdleHandler.cs`, **`RpcServiceRegistry.cs` (A2 new)** |
| **Server loop alias** | `CRpcLoopHost.cs` → thin forward to `OrientLoopHost.RunUntilCancelled` (optional obsolete) |
| **Client** | All under `Rpc/CRpc/Client/` |
| **Client loop alias** | `CRpcClientLoopHost.cs` → forward to `OrientLoopHost` |
| **Codec** | All under `Rpc/CRpc/Codec/` + `crpc-options.proto` |
| **Transport** | `Transport/*.cs` |
| **Protocol** | `CRpcStatusCode.cs` |
| **Util** | `ChannelWriteUtil.cs`, `NetworkHelper.cs` |
| **Client abstraction** | `IRPCClient.cs` → `Orient.Rpc.Client` (it depends on client and codec types) |
| **Nacos** | `ConfigCenter/`, `Registry/`, `Mgr/` |

**Orient.Rpc.csproj:**

```xml
<ItemGroup>
  <ProjectReference Include="..\Orient.Runtime\Orient.Runtime.csproj" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="DotNetty.*" … />
  <PackageReference Include="Google.Protobuf" … />
  <PackageReference Include="nacos-sdk-csharp" … />
  <PackageReference Include="Microsoft.Extensions.Logging.Console" … />
</ItemGroup>
```

**Delete after migration:** `CRPC/CRPC.csproj` (or `CRpc/CRPC.csproj`) and empty tree.

---

## Namespace Mapping

| Layer | Root namespace | Example |
| --- | --- | --- |
| Runtime | `Orient.Runtime` | `Orient.Runtime.OrientLoop` |
| Rpc abstractions | `Orient.Rpc` | `Orient.Rpc.IRpcService` |
| Rpc server | `Orient.Rpc.Server` | `Orient.Rpc.Server.CRpcServer` |
| Rpc client | `Orient.Rpc.Client` | `Orient.Rpc.Client.CRpcClient` |
| Rpc codec | `Orient.Rpc.Codec` | `Orient.Rpc.Codec.CRpcMessage` |
| Rpc protocol | `Orient.Rpc.Protocol` | `Orient.Rpc.Protocol.CRpcStatusCode` |
| Rpc util | `Orient.Rpc.Util` | `Orient.Rpc.Util.ChannelWriteUtil` |
| Transport | `Orient.Rpc.Transport` | `Orient.Rpc.Transport.TcpChannelHost` |

Global find-replace `CRpc.Async` → `Orient.Runtime`, `CRpc.Rpc.CRpc` → appropriate `Orient.Rpc.*` subnamespace.

Protocol-level shared types (`CRpcStatusCode`) live under `Orient.Rpc.Protocol`. Transport helpers (`ChannelWriteUtil`, `NetworkHelper`) live under `Orient.Rpc.Util`. Public client-facing abstractions that depend on client/codec types (`IRPCClient`) live under `Orient.Rpc.Client`, not under root abstractions.

---

## OrientLoopHost Consolidation

`CRpcServerLoop` and `CRpcClientLoop` are byte-for-byte equivalent aside from log prefix. Replace with:

```csharp
namespace Orient.Runtime;

public static class OrientLoopHost
{
    public static void RunUntilCancelled(OrientLoop loop, CancellationToken cancellationToken);
}
```

`Orient.Rpc.Server.CRpcLoopHost` and `Orient.Rpc.Client.CRpcClientLoopHost` become one-liners calling `OrientLoopHost.RunUntilCancelled` for backward-compatible entry points (may mark `[Obsolete]` directing to `OrientLoopHost` in a later cleanup).

Non-RPC hosts (`DataManager`, future Center) call `OrientLoopHost` directly without referencing `Orient.Rpc`.

---

## Consumer ProjectReference Updates

| Project | Before | After |
| --- | --- | --- |
| `HelloWorldServer` | `CRPC.csproj` | `Orient.Rpc.csproj` |
| `HelloWorldClient` | `CRPC.csproj` | `Orient.Rpc.csproj` |
| `GateWay.Core` | `CRPC.csproj` | `Orient.Rpc.csproj` |
| `GateWayServer` / `GateWayClient` | `CRPC.csproj` | `Orient.Rpc.csproj` |
| `CRPC.Tests` | `CRPC.csproj` | `Orient.Rpc.csproj` (+ optional direct `Orient.Runtime` if split tests) |
| `CRPC.TestHelper` | `CRPC.csproj` | `Orient.Runtime.csproj`; rename to `Orient.TestHelper` if implementation scope allows, otherwise keep project name for this delivery and update namespace/usings |
| **Future `Orient.DataManager`** | — | **`Orient.Runtime` only** |

**Using statements in application code:** update `CRpc.Async` → `Orient.Runtime`; `CRpc.Rpc.*` → `Orient.Rpc.*`.

---

## Protobuf Plugin / Codegen

Generated service bases reference `IRpcService`, `OrientTask`, `IRpcContext`, `IRpcMessage`. After split, update `Tool/crpc-protobuf-plugin` emitted usings:

- `using Orient.Runtime;`
- `using Orient.Rpc;`

Plugin project may need `ProjectReference` to `Orient.Rpc` (or both packages). Regenerate HelloWorld protos in verification.

The plugin project itself remains in `Tool/crpc-protobuf-plugin`; only its generated output and any compile-time references required for tests are updated.

---

## Testing Strategy

### Phase 1 (same delivery as split)

- Keep single test project `CRPC.Tests` (name may remain for CI stability) referencing `Orient.Rpc`.
- Migrate `CRpcLoopRegistryTests` → `RpcServiceRegistryTests` per A2 spec.
- Rename test types/usings: `CRpcLoop` → `OrientLoop`, etc.
- Loop-focused tests (`OrientLoopTickOrderTests`, `MinHeapTimerSchedulerTests`, …) remain valid; namespaces updated.

### Phase 2 (optional follow-up)

- Split into `Orient.Runtime.Tests` and `Orient.Rpc.Tests` for faster isolated runs.

**Verification gate:** full existing test suite green after split + rename + A2.

---

## Documentation Updates

| Document | Change |
| --- | --- |
| `Doc/architecture.md` | ServiceRegistry on `RpcServiceRegistry`; Runtime split; `OrientLoop` terminology |
| `Doc/TODO.txt` | Archive completed split item if present |
| A2 spec | Cross-link this spec; note combined implementation |
| Example README / comments | `CRpcLoop` → `OrientLoop`, `server.Services.Register` |

---

## Migration / Rollout

Single PR (recommended):

1. Implement A2 (`RpcServiceRegistry`, remove loop registry).
2. Create `Orient.Runtime` + `Orient.Rpc`; move files; rename types.
3. Add `OrientLoopHost`; wire Rpc loop host aliases.
4. Update solution, examples, tests, plugin usings.
5. Delete monolithic `CRPC.csproj`.
6. `dotnet build` + `dotnet test` on `orient-dotnet.sln`.

**No compatibility shim assembly:** do not keep `CRPC.dll` as a facade; all in-repo consumers update in the same PR.

---

## Invariants

1. `Orient.Runtime` has zero NuGet dependencies beyond SDK/BCL.
2. `OrientLoop` has no RPC types or service registry.
3. `RpcServiceRegistry` lives in `Orient.Rpc`; owned by `CRpcServer.Services` (A2).
4. All registry and RPC dispatch thread rules unchanged (owner loop thread).
5. `OrientTask` completion and `OrientTaskCompletionSource` loop-thread rules unchanged from current `CRpcTask` behavior.

---

## Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Large rename diff | Single PR; use structured rename in IDE; split test runs per project |
| Plugin generates old usings | Update plugin + regenerate in same PR |
| External consumers of `CRPC` NuGet (if any) | In-repo only today; document breaking change in release notes |
| GateWay / HelloWorld missed usings | CI build all examples |

---

## Verification Checklist

- [ ] `Orient.Runtime` builds with no DotNetty/Protobuf/Nacos references
- [ ] `Orient.Rpc` builds and references only `Orient.Runtime` + external packages
- [ ] Monolithic `CRPC` project removed
- [ ] No remaining `CRpc.Async` namespace in repo (except git history)
- [ ] No `loop.RegisterService` (A2)
- [ ] `dotnet test` — all tests pass
- [ ] HelloWorld server/client run
- [ ] GateWay server/client run
- [ ] Empty `Orient.DataManager` class library can reference `Orient.Runtime` only and compile

---

## Relationship to A2 Spec

| Concern | Owner spec |
| --- | --- |
| Registry off loop, `server.Services` API | A2 |
| Physical projects, rename, loop host merge | This spec |
| Combined implementation plan | Next step (`writing-plans`) |

Both specs should be **Approved** before implementation plan is written.

---

## Open Items (post-split)

- `Orient.DataManager` spec and project
- Optional test project split (`Orient.Runtime.Tests`)
- Remove obsolete `CRpcLoopHost` / `CRpcClientLoopHost` aliases after one release cycle
- Consider moving Nacos to `Orient.Integration.Nacos` if Rpc package should slim further
