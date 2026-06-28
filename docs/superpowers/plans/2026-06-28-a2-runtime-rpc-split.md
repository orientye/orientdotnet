# A2 + Orient.Runtime / Orient.Rpc Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the RPC service registry from `CRpcLoop` onto `CRpcServer.Services`, then split the monolithic `CRPC` assembly into `Orient.Runtime` (BCL-only execution) and `Orient.Rpc` (wire protocol + transport), renaming execution types to `OrientLoop` / `OrientTask`.

**Architecture:** Phase 1 implements A2 inside the existing `CRpc/` tree so tests stay green on a familiar layout. Phase 2 creates `Orient.Runtime` and `Orient.Rpc`, moves files with namespace/type renames, consolidates `CRpcServerLoop` + `CRpcClientLoop` into `OrientLoopHost`, updates all consumers and the protobuf plugin, then deletes `CRpc/CRPC.csproj`. No compatibility shim assembly.

**Tech Stack:** C# / .NET 8, DotNetty 0.7.6, Google.Protobuf, nacos-sdk-csharp, xUnit, `dotnet build` / `dotnet test`.

**Spec references:**
- `docs/superpowers/specs/2026-06-28-a2-rpc-service-registry-design.md`
- `docs/superpowers/specs/2026-06-28-orient-runtime-rpc-split-design.md`

**Repository rules:** Use `OrientTask` / `OrientLoop` (after rename) for async — not `System.Threading.Tasks.Task` for project APIs. Do not create git commits unless the user explicitly requests them.

---

## Prerequisites (before Task 1)

- [ ] Both specs reviewed and approved for execution by the user:
  - `docs/superpowers/specs/2026-06-28-a2-rpc-service-registry-design.md`
  - `docs/superpowers/specs/2026-06-28-orient-runtime-rpc-split-design.md`
- [ ] Do **not** change spec `Status` to `Approved` during implementation; approval is a pre-execution decision (see Task 10).

**Test green zones:**

| Phase | Tasks | Expectation |
| --- | --- | --- |
| A2 gate | Task 1–3 | `dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj` must PASS |
| Split migration | Task 4–7 | Treat as one atomic batch; monolith breaks after Task 4 Step 2 until Task 7 completes |
| Full gate | Task 8–11 | `dotnet test orient-dotnet.sln` must PASS |

After Task 4 Step 2 moves `CRpc/Async/` out of the monolith, do **not** expect `CRPC.Tests` or `CRPC.csproj` to build until Task 7 updates project references. Task 7 Step 4 validates `dotnet build` only; generator tests may still fail until Task 8.

---

## File Structure (target state)

| Path | Responsibility |
| --- | --- |
| `Orient.Runtime/Orient.Runtime.csproj` | BCL-only; `OrientLoop`, `OrientTask`, timers, `OrientLoopRunner`, `OrientLoopHost` |
| `Orient.Runtime/Loop/OrientLoop.cs` | Execution loop; **no** `IRpcService`, **no** registry |
| `Orient.Runtime/Loop/OrientLoopHost.cs` | Merged `RunUntilCancelled` driver (replaces `CRpcServerLoop` / `CRpcClientLoop`) |
| `Orient.Runtime/Task/OrientTask*.cs` | Custom async primitive + builders |
| `Orient.Runtime/Timer/*.cs` | Timer scheduler |
| `Orient.Rpc/Orient.Rpc.csproj` | References `Orient.Runtime` + DotNetty/Protobuf/Nacos |
| `Orient.Rpc/Server/RpcServiceRegistry.cs` | Loop-thread service registry (A2) |
| `Orient.Rpc/Server/CRpcServer.cs` | Transport endpoint; owns `Services` |
| `Orient.Rpc/Server/CRpcServerHandler.cs` | Dispatch via `server.Services.TryGet` |
| `Orient.Rpc/Interfaces/IRpcService.cs` | RPC contracts (not in Runtime) |
| `Orient.Rpc/Client/*.cs` | Client, references, push |
| `Orient.Rpc/Codec/*.cs` | Frame codec |
| `Orient.Rpc/Protocol/CRpcStatusCode.cs` | Protocol-level shared types |
| `Orient.Rpc/Util/ChannelWriteUtil.cs` | Transport write helpers |
| `Orient.Rpc/Transport/*.cs` | `TcpChannelHost`, `LoopInboundHandler` |
| `Orient.Rpc/Util/NetworkHelper.cs` | Nacos local IP helper |
| `CRPC.TestHelper/` | References `Orient.Runtime` only |
| `Tests/CRPC.Tests/` | References `Orient.Rpc` (transitive Runtime) |
| `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcGen.cs` | Emits `using Orient.Runtime;` / `using Orient.Rpc.*` |

**Delete after migration:** `CRpc/` tree and `CRpc/CRPC.csproj`.

---

## Task 1: RpcServiceRegistry (TDD in monolith)

**Files:**
- Create: `CRpc/Rpc/CRpc/Server/RpcServiceRegistry.cs`
- Create: `Tests/CRPC.Tests/RpcServiceRegistryTests.cs`
- Delete: `Tests/CRPC.Tests/CRpcLoopRegistryTests.cs` (after migration)

- [ ] **Step 1: Add failing `RpcServiceRegistryTests`**

Create `Tests/CRPC.Tests/RpcServiceRegistryTests.cs` by copying `CRpcLoopRegistryTests.cs` and adapting:

```csharp
using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Server;

namespace CRPC.Tests;

public class RpcServiceRegistryTests : CrpcTestBase
{
    [Fact]
    public void RegisterRequiresLoopThread()
    {
        var loop = new CRpcLoop();
        var registry = new RpcServiceRegistry(loop);
        var service = new RecordingService(1000);

        Assert.Throws<InvalidOperationException>(() => registry.Register(service));
    }

    [Fact]
    public void RegisterAndTryGetOnLoopThread()
    {
        var loop = new CRpcLoop();
        var registry = new RpcServiceRegistry(loop);
        var service = new RecordingService(1000);
        loop.Post(() => registry.Register(service));
        loop.Tick();
        loop.Post(() =>
        {
            Assert.True(registry.TryGet(1000, out var found));
            Assert.Same(service, found);
        });
        loop.Tick();
    }

    [Fact]
    public void UnregisterRemovesService()
    {
        var loop = new CRpcLoop();
        var registry = new RpcServiceRegistry(loop);
        var service = new RecordingService(1000);
        loop.Post(() =>
        {
            registry.Register(service);
            registry.Unregister(service);
            Assert.False(registry.TryGet(1000, out _));
        });
        loop.Tick();
    }

    [Fact]
    public void UnregisterDoesNotRemoveReplacementForSameServiceId()
    {
        const ushort serviceId = 1001;
        var loop = new CRpcLoop();
        var registry = new RpcServiceRegistry(loop);
        var oldService = new RecordingService(serviceId);
        var newService = new RecordingService(serviceId);
        loop.Post(() =>
        {
            registry.Register(oldService);
            registry.Register(newService);
            registry.Unregister(oldService);
            Assert.True(registry.TryGet(serviceId, out var found));
            Assert.Same(newService, found);
        });
        loop.Tick();
    }

    [Fact]
    public void DifferentRegistriesOnDifferentLoopsDoNotCollide()
    {
        const ushort serviceId = 1002;
        var firstLoop = new CRpcLoop();
        var secondLoop = new CRpcLoop();
        var firstRegistry = new RpcServiceRegistry(firstLoop);
        var secondRegistry = new RpcServiceRegistry(secondLoop);
        var firstService = new RecordingService(serviceId);
        var secondService = new RecordingService(serviceId);

        DedicatedLoopThread.Run(firstLoop, loop =>
        {
            loop.Post(() => firstRegistry.Register(firstService));
            loop.Tick();
            Assert.True(firstRegistry.TryGet(serviceId, out var found));
            Assert.Same(firstService, found);
        });

        DedicatedLoopThread.Run(secondLoop, loop =>
        {
            loop.Post(() => secondRegistry.Register(secondService));
            loop.Tick();
            Assert.True(secondRegistry.TryGet(serviceId, out var found));
            Assert.Same(secondService, found);
        });
    }

    [Fact]
    public void ClearRemovesAllServices()
    {
        var loop = new CRpcLoop();
        var registry = new RpcServiceRegistry(loop);
        var service = new RecordingService(1003);
        loop.Post(() =>
        {
            registry.Register(service);
            registry.Clear();
            Assert.False(registry.TryGet(service.GetServiceId(), out _));
        });
        loop.Tick();
    }

    private sealed class RecordingService : IRpcService
    {
        private readonly ushort serviceId;

        public RecordingService(ushort serviceId) => this.serviceId = serviceId;

        public ushort GetServiceId() => serviceId;

        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req) =>
            CRpcTask.FromResult((0, Array.Empty<byte>()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd d:\orient\my\orientdotnet
dotnet build Tests/CRPC.Tests/CRPC.Tests.csproj
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~RpcServiceRegistryTests" --no-build
```

Expected: FAIL — `RpcServiceRegistry` type not found.

- [ ] **Step 3: Implement `RpcServiceRegistry`**

Create `CRpc/Rpc/CRpc/Server/RpcServiceRegistry.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using CRpc.Async;
using CRpc.Rpc;

namespace CRpc.Rpc.CRpc.Server;

public sealed class RpcServiceRegistry
{
    private const int InitialServiceCapacity = 106;

    private readonly CRpcLoop loop;
    private readonly Dictionary<ushort, IRpcService> services = new(InitialServiceCapacity);

    public RpcServiceRegistry(CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        this.loop = loop;
    }

    public void Register(IRpcService service)
    {
        loop.EnsureInLoopThread();
        ArgumentNullException.ThrowIfNull(service);
        services[service.GetServiceId()] = service;
    }

    public bool TryGet(ushort serviceId, [MaybeNullWhen(false)] out IRpcService service)
    {
        loop.EnsureInLoopThread();
        return services.TryGetValue(serviceId, out service);
    }

    public void Unregister(IRpcService service)
    {
        loop.EnsureInLoopThread();
        ArgumentNullException.ThrowIfNull(service);
        var serviceId = service.GetServiceId();
        if (services.TryGetValue(serviceId, out var registeredService)
            && ReferenceEquals(registeredService, service))
        {
            services.Remove(serviceId);
        }
    }

    public void Clear()
    {
        loop.EnsureInLoopThread();
        services.Clear();
    }
}
```

Note: `EnsureInLoopThread` is `internal` on `CRpcLoop` today — registry is in the same assembly (`CRPC`), so it compiles. After split, `Orient.Rpc` needs `InternalsVisibleTo` or a public/internal accessor on `OrientLoop`.

- [ ] **Step 4: Run registry tests**

```bash
dotnet build Tests/CRPC.Tests/CRPC.Tests.csproj
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~RpcServiceRegistryTests" --no-build
```

Expected: PASS.

- [ ] **Step 5: Relocate loop binding tests, then delete `CRpcLoopRegistryTests.cs`**

Before deleting `Tests/CRPC.Tests/CRpcLoopRegistryTests.cs`, move these two tests into a new file `Tests/CRPC.Tests/OrientLoopThreadBindingTests.cs` (class name may stay `OrientLoopThreadBindingTests` even during monolith phase):

```csharp
using CRpc.Async;

namespace CRPC.Tests;

public class OrientLoopThreadBindingTests : CrpcTestBase
{
    [Fact]
    public void TickOnWrongThreadThrowsAfterBind()
    {
        var loop = new CRpcLoop();
        DedicatedLoopThread.Run(loop, _ => { });

        var exception = Assert.Throws<InvalidOperationException>(() => loop.Tick());
        Assert.Contains("loop thread", exception.Message, StringComparison.Ordinal);
    }

#if DEBUG
    [Fact]
    public void BindSecondLoopOnSameThreadThrowsInDebug()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var firstLoop = new CRpcLoop();
                var secondLoop = new CRpcLoop();
                firstLoop.BindToCurrentThread();
                secondLoop.BindToCurrentThread();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();
        thread.Join();

        var exception = Assert.IsType<InvalidOperationException>(captured);
        Assert.Contains("already bound", exception.Message, StringComparison.Ordinal);
    }
#endif
}
```

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~OrientLoopThreadBindingTests"
```

Expected: PASS.

Then delete `Tests/CRPC.Tests/CRpcLoopRegistryTests.cs`.

---

## Task 2: Wire `CRpcServer.Services` and handlers

**Files:**
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServer.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs`
- Modify: `Example/GateWay/GateWay.Core/GateWayServerHandler.cs`

- [ ] **Step 1: Add `Services` to `CRpcServer`**

In `CRpcServer` constructor, after `Loop = loop`:

```csharp
Services = new RpcServiceRegistry(loop);
```

Add property:

```csharp
public RpcServiceRegistry Services { get; }
```

- [ ] **Step 2: Update `CRpcServerHandler` dispatch**

In `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs`, replace:

```csharp
if (server.Loop.TryGetService(serviceId, out var rpcService))
```

with:

```csharp
if (server.Services.TryGet(serviceId, out var rpcService))
```

- [ ] **Step 3: Update `GateWayServerHandler`**

In `Example/GateWay/GateWay.Core/GateWayServerHandler.cs`, replace both:

```csharp
server.Loop.TryGetService(...)
```

with:

```csharp
server.Services.TryGet(...)
```

- [ ] **Step 4: Build GateWay.Core**

```bash
dotnet build Example/GateWay/GateWay.Core/GateWay.Core.csproj
```

Expected: PASS (handlers compile; loop registry still exists).

---

## Task 3: Remove registry from `CRpcLoop` and migrate call sites

**Files:**
- Modify: `CRpc/Async/CRpcLoop.cs` — remove `using CRpc.Rpc`, `registeredServices`, `RegisterService`, `TryGetService`, `UnregisterService`, `ClearRegisteredServices`, `InitialServiceCapacity`
- Modify: `Example/HelloWorld/Server/Program.cs`
- Modify: `Example/GateWay/GateWayServer/Program.cs`
- Modify: `Tests/CRPC.Tests/CRpcServerTests.cs`
- Modify: `Tests/CRPC.Tests/RpcServiceInvokerTests.cs`
- Modify: `Tests/CRPC.Tests/CRpcServerHandlerTests.cs`
- Modify: `Tests/CRPC.Tests/CRpcServerPushIntegrationTests.cs`
- Modify: `Tests/CRPC.Tests/GateWay/GateWayServerHandlerTests.cs`

- [ ] **Step 1: Remove registry from `CRpcLoop`**

Delete registry field and all four registry methods from `CRpc/Async/CRpcLoop.cs`. Remove `using CRpc.Rpc;`.

- [ ] **Step 2: Update `CRpcServerTests.StopAsyncPreservesRegisteredServices`**

Replace:

```csharp
loop.RegisterService(service);
// ...
Assert.True(loop.TryGetService(serviceId, out var found));
```

with:

```csharp
server.Services.Register(service);
// ...
Assert.True(server.Services.TryGet(serviceId, out var found));
```

- [ ] **Step 3: Update example hosts**

`Example/HelloWorld/Server/Program.cs` — inside `CRpcLoopRunner.RunUntilComplete`:

```csharp
crpcServer.Services.Register(impl);
```

(replace `loop.RegisterService(impl)`).

`Example/GateWay/GateWayServer/Program.cs` — same pattern with `server.Services.Register(...)`.

- [ ] **Step 4: Update remaining tests**

First update `Tests/CRPC.Tests/RpcServiceInvokerTests.cs`: delete the line below, because `RpcServiceInvoker.InvokeAsync` already receives the `IRpcService` directly and does not use a registry lookup.

```csharp
loop.RegisterService(service);
```

Then update every remaining test file that calls `loop.RegisterService`:

```csharp
// Before
loop.Post(() => loop.RegisterService(service));

// After — when CRpcServer is in scope
loop.Post(() => server.Services.Register(service));
```

Files to update (grep `RegisterService` / `TryGetService` under `Tests/`):
- `RpcServiceInvokerTests.cs`
- `CRpcServerHandlerTests.cs`
- `CRpcServerPushIntegrationTests.cs`
- `GateWay/GateWayServerHandlerTests.cs`

Use this search after edits:

```bash
rg "RegisterService|TryGetService|UnregisterService|ClearRegisteredServices" Tests --glob "*.cs"
```

Expected: only `RpcServiceRegistryTests.cs` contains registry operation names, and it uses `registry.Register` / `registry.TryGet`, not `loop.RegisterService` / `loop.TryGetService`.

- [ ] **Step 5: Verify no loop registry references remain**

```bash
cd d:\orient\my\orientdotnet
rg "loop\.(RegisterService|TryGetService|UnregisterService|ClearRegisteredServices)" --glob "*.cs"
```

Expected: no matches in `CRpc/`, `Tests/`, `Example/`.

- [ ] **Step 6: Run full test suite (A2 gate)**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: all tests PASS.

---

## Tasks 4–7: Split migration (atomic batch)

Complete Tasks 4, 5, 6, and 7 in one continuous session before running the full test suite again. Task 4 Step 2 physically moves `CRpc/Async/` out of the monolith; `CRPC.csproj` and `CRPC.Tests` remain broken until Task 7 retargets references to `Orient.Runtime` / `Orient.Rpc`.

Checkpoint builds during this batch:

```bash
dotnet build Orient.Runtime/Orient.Runtime.csproj    # after Task 4
dotnet build Orient.Rpc/Orient.Rpc.csproj            # after Task 6
dotnet build orient-dotnet.sln                       # after Task 7
```

Do not run `dotnet test` until Task 8 completes (generator output must match renamed types).

---

## Task 4: Create `Orient.Runtime` project

**Files:**
- Create: `Orient.Runtime/Orient.Runtime.csproj`
- Move+rename from `CRpc/Async/` → `Orient.Runtime/Loop/`, `Orient.Runtime/Task/`, `Orient.Runtime/Timer/`

- [ ] **Step 1: Create `Orient.Runtime.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="Orient.Rpc" />
    <InternalsVisibleTo Include="CRPC.Tests" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>Orient.Runtime</AssemblyName>
    <RootNamespace>Orient.Runtime</RootNamespace>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Move and rename Async files**

| From | To |
| --- | --- |
| `CRpc/Async/CRpcLoop.cs` | `Orient.Runtime/Loop/OrientLoop.cs` |
| `CRpc/Async/CRpcLoopOptions.cs` | `Orient.Runtime/Loop/OrientLoopOptions.cs` |
| `CRpc/Async/CRpcLoopRunner.cs` | `Orient.Runtime/Loop/OrientLoopRunner.cs` |
| `CRpc/Async/CRpcLoopTimer.cs` | `Orient.Runtime/Timer/OrientLoopTimer.cs` |
| `CRpc/Async/ICRpcLoopTimerScheduler.cs` | `Orient.Runtime/Timer/IOrientLoopTimerScheduler.cs` |
| `CRpc/Async/MinHeapTimerScheduler.cs` | `Orient.Runtime/Timer/MinHeapTimerScheduler.cs` |
| `CRpc/Async/CRpcTask.cs` | `Orient.Runtime/Task/OrientTask.cs` |
| `CRpc/Async/CRpcTask.Generic.cs` | `Orient.Runtime/Task/OrientTask.Generic.cs` |
| `CRpc/Async/CRpcTaskStatus.cs` | `Orient.Runtime/Task/OrientTaskStatus.cs` |
| `CRpc/Async/CRpcTaskCompletionSource.cs` | `Orient.Runtime/Task/OrientTaskCompletionSource.cs` |
| `CRpc/Async/CRpcAsyncMethodBuilder.cs` | `Orient.Runtime/Task/OrientAsyncMethodBuilder.cs` |
| `CRpc/Async/CRpcAsyncMethodBuilder.Generic.cs` | `Orient.Runtime/Task/OrientAsyncMethodBuilder.Generic.cs` |

In each moved file:
- `namespace CRpc.Async` → `namespace Orient.Runtime`
- `CRpcLoop` → `OrientLoop` (all occurrences in Runtime files)
- `CRpcTask` → `OrientTask`
- `CRpcLoopRunner` → `OrientLoopRunner`
- `CRpcLoopOptions` → `OrientLoopOptions`
- `CRpcLoopTimer` → `OrientLoopTimer`
- `ICRpcLoopTimerScheduler` → `IOrientLoopTimerScheduler`
- `CRpcTaskCompletionSource` → `OrientTaskCompletionSource`
- `CRpcAsyncMethodBuilder` → `OrientAsyncMethodBuilder`
- Exception messages: `"CRpcLoop"` → `"OrientLoop"` where user-facing

- [ ] **Step 3: Build Runtime alone**

```bash
dotnet build Orient.Runtime/Orient.Runtime.csproj
```

Expected: PASS with zero NuGet package references.

---

## Task 5: Create `OrientLoopHost` (Runtime)

**Files:**
- Create: `Orient.Runtime/Loop/OrientLoopHost.cs`
- (Later) thin aliases in `Orient.Rpc`

- [ ] **Step 1: Add `OrientLoopHost`**

Create `Orient.Runtime/Loop/OrientLoopHost.cs` (merge logic from `CRpc/Rpc/CRpc/Server/CRpcServerLoop.cs`):

```csharp
namespace Orient.Runtime;

public static class OrientLoopHost
{
    public static void RunUntilCancelled(OrientLoop loop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loop);
        loop.BindToCurrentThread();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                loop.Tick();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"OrientLoopHost: unexpected exception escaped Tick: {exception}");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                loop.WaitForWorkOrTimer(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
```

- [ ] **Step 2: Rebuild Runtime**

```bash
dotnet build Orient.Runtime/Orient.Runtime.csproj
```

Expected: PASS.

---

## Task 6: Create `Orient.Rpc` and move remaining files

**Files:**
- Create: `Orient.Rpc/Orient.Rpc.csproj`
- Move non-Async files from `CRpc/` into `Orient.Rpc/` subfolders per split spec, excluding loop driver implementation files listed below
- Delete: `CRpc/Rpc/CRpc/Server/CRpcServerLoop.cs`, `CRpc/Rpc/CRpc/Client/CRpcClientLoop.cs` (replaced by `OrientLoopHost`)
- Rewrite: `CRpc/Rpc/CRpc/Server/CRpcLoopHost.cs`, `CRpc/Rpc/CRpc/Client/CRpcClientLoopHost.cs` as aliases in `Orient.Rpc`

- [ ] **Step 1: Create `Orient.Rpc.csproj`**

Copy package references from `CRpc/CRPC.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="CRPC.Tests" />
    <InternalsVisibleTo Include="GateWay.Core" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>Orient.Rpc</AssemblyName>
    <RootNamespace>Orient.Rpc</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Orient.Runtime\Orient.Runtime.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="DotNetty.Buffers" Version="0.7.6" />
    <PackageReference Include="DotNetty.Codecs" Version="0.7.6" />
    <PackageReference Include="DotNetty.Common" Version="0.7.6" />
    <PackageReference Include="DotNetty.Handlers" Version="0.7.6" />
    <PackageReference Include="DotNetty.Transport" Version="0.7.6" />
    <PackageReference Include="Google.Protobuf" Version="3.35.0-rc1" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="11.0.0-preview.3.26207.106" />
    <PackageReference Include="nacos-sdk-csharp" Version="1.3.10" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Move Rpc files with namespace mapping**

Suggested layout and namespace renames:

| Source | Destination | Namespace |
| --- | --- | --- |
| `CRpc/Rpc/IRPCService.cs` | `Orient.Rpc/Interfaces/IRpcService.cs` | `Orient.Rpc` |
| `CRpc/Rpc/IRPCContext.cs` | `Orient.Rpc/Interfaces/IRpcContext.cs` | `Orient.Rpc` |
| `CRpc/Rpc/IRpcMessage.cs` | `Orient.Rpc/Interfaces/IRpcMessage.cs` | `Orient.Rpc` |
| `CRpc/Rpc/IRPCClient.cs` | `Orient.Rpc/Client/IRpcClient.cs` | `Orient.Rpc.Client` |
| `CRpc/Rpc/CRpc/Server/*.cs` | `Orient.Rpc/Server/` | `Orient.Rpc.Server` |
| `CRpc/Rpc/CRpc/Client/*.cs` | `Orient.Rpc/Client/` | `Orient.Rpc.Client` |
| `CRpc/Rpc/CRpc/Codec/*.cs` | `Orient.Rpc/Codec/` | `Orient.Rpc.Codec` |
| `CRpc/Rpc/CRpc/Protobuf/crpc-options.proto` | `Orient.Rpc/Codec/crpc-options.proto` | — |
| `CRpc/Rpc/CRpc/ChannelWriteUtil.cs` | `Orient.Rpc/Util/ChannelWriteUtil.cs` | `Orient.Rpc.Util` |
| `CRpc/Rpc/CRpc/CRpcStatusCode.cs` | `Orient.Rpc/Protocol/CRpcStatusCode.cs` | `Orient.Rpc.Protocol` |
| `CRpc/Transport/*.cs` | `Orient.Rpc/Transport/` | `Orient.Rpc.Transport` |
| `CRpc/Util/NetworkHelper.cs` | `Orient.Rpc/Util/NetworkHelper.cs` | `Orient.Rpc.Util` |
| `CRpc/ConfigCenter/` | `Orient.Rpc/ConfigCenter/` | `Orient.Rpc.ConfigCenter` |
| `CRpc/Registry/` | `Orient.Rpc/Registry/` | `Orient.Rpc.Registry` |
| `CRpc/Mgr/` | `Orient.Rpc/Mgr/` | `Orient.Rpc.Mgr` |

Do not move these implementation files:

| Source | Action |
| --- | --- |
| `CRpc/Rpc/CRpc/Server/CRpcServerLoop.cs` | Delete after `OrientLoopHost` exists |
| `CRpc/Rpc/CRpc/Client/CRpcClientLoop.cs` | Delete after `OrientLoopHost` exists |
| `CRpc/Rpc/CRpc/Server/CRpcLoopHost.cs` | Replace with alias shown in Step 3 |
| `CRpc/Rpc/CRpc/Client/CRpcClientLoopHost.cs` | Replace with alias shown in Step 3 |

In every moved Rpc file:
- `using CRpc.Async` → `using Orient.Runtime`
- `CRpcLoop` → `OrientLoop`
- `CRpcTask` → `OrientTask`
- `CRpcLoopRunner` → `OrientLoopRunner`
- `namespace CRpc.Rpc` → `namespace Orient.Rpc` (abstractions)
- `namespace CRpc.Rpc.CRpc.Server` → `namespace Orient.Rpc.Server`
- `namespace CRpc.Rpc.CRpc.Client` → `namespace Orient.Rpc.Client`
- `namespace CRpc.Rpc.CRpc.Codec` → `namespace Orient.Rpc.Codec`
- `namespace CRpc.Rpc.CRpc` → `namespace Orient.Rpc.Protocol` (status code), `Orient.Rpc.Util` (channel util)
- `namespace CRpc.Transport` → `namespace Orient.Rpc.Transport`

Update `RpcServiceRegistry` constructor parameter: `OrientLoop loop`.

- [ ] **Step 3: Add Rpc loop host aliases**

Create `Orient.Rpc/Server/CRpcLoopHost.cs`:

```csharp
using Orient.Runtime;

namespace Orient.Rpc.Server;

public static class CRpcLoopHost
{
    public static void RunUntilCancelled(OrientLoop loop, CancellationToken cancellationToken) =>
        OrientLoopHost.RunUntilCancelled(loop, cancellationToken);
}
```

Create `Orient.Rpc/Client/CRpcClientLoopHost.cs`:

```csharp
using Orient.Runtime;

namespace Orient.Rpc.Client;

public static class CRpcClientLoopHost
{
    public static void RunUntilCancelled(OrientLoop loop, CancellationToken cancellationToken) =>
        OrientLoopHost.RunUntilCancelled(loop, cancellationToken);
}
```

Do **not** move `CRpcServerLoop.cs` / `CRpcClientLoop.cs` — delete them.

- [ ] **Step 4: Build Orient.Rpc**

```bash
dotnet build Orient.Rpc/Orient.Rpc.csproj
```

Expected: PASS. If it fails due to stale namespace imports, run:

```bash
rg "using CRpc\.|namespace CRpc\." Orient.Rpc --glob "*.cs"
```

Expected after corrections: no matches in `Orient.Rpc/`.

---

## Task 7: Update solution and consumer projects

**Files:**
- Modify: `orient-dotnet.sln`
- Modify: `Tests/CRPC.Tests/CRPC.Tests.csproj`
- Modify: `CRPC.TestHelper/CRPC.TestHelper.csproj`
- Modify: `Example/HelloWorld/Server/HelloWorldServer.csproj`
- Modify: `Example/HelloWorld/Client/HelloWorldClient.csproj`
- Modify: `Example/GateWay/GateWay.Core/GateWay.Core.csproj`
- Modify: `Example/GateWay/Client/GateWayClient.csproj`
- Modify: `.cs` files under `Tests/`, `Example/`, `CRPC.TestHelper/` that match the Step 3 search command

- [ ] **Step 1: Update solution**

In `orient-dotnet.sln`:
- Remove `CRPC` project entry (`CRPC\CRPC.csproj`)
- Add `Orient.Runtime` and `Orient.Rpc` projects

Fix path note: current sln references `CRPC\CRPC.csproj` but on disk the monolith is `CRpc\CRPC.csproj`.

- [ ] **Step 2: Update project references**

| Project | Change |
| --- | --- |
| `Tests/CRPC.Tests/CRPC.Tests.csproj` | `..\..\CRPC\CRPC.csproj` → `..\..\Orient.Rpc\Orient.Rpc.csproj` |
| `CRPC.TestHelper/CRPC.TestHelper.csproj` | `..\CRPC\CRPC.csproj` → `..\Orient.Runtime\Orient.Runtime.csproj` |
| `Example/HelloWorld/Server/HelloWorldServer.csproj` | → `Orient.Rpc.csproj` |
| `Example/HelloWorld/Client/HelloWorldClient.csproj` | → `Orient.Rpc.csproj` |
| `Example/GateWay/GateWay.Core/GateWay.Core.csproj` | → `Orient.Rpc.csproj` |
| `Example/GateWay/Client/GateWayClient.csproj` | → `Orient.Rpc.csproj` |

- [ ] **Step 3: Global using / namespace sweep**

Find files to update:

```bash
rg "using CRpc\.|CRpcLoop|CRpcTask|CRpcLoopRunner|CRpcServerLoop|CRpcClientLoopHost|CRpcLoopHost" Tests Example CRPC.TestHelper --glob "*.cs" --files-with-matches
```

In the files returned by that command, replace:

```
using CRpc.Async;              → using Orient.Runtime;
using CRpc.Rpc;                → using Orient.Rpc;
using CRpc.Rpc.CRpc.Server;    → using Orient.Rpc.Server;
using CRpc.Rpc.CRpc.Client;    → using Orient.Rpc.Client;
using CRpc.Rpc.CRpc.Codec;     → using Orient.Rpc.Codec;
using CRpc.Rpc.CRpc;           → using Orient.Rpc.Protocol; / using Orient.Rpc.Util;
using CRpc.Transport;          → using Orient.Rpc.Transport;
```

Type renames in those trees:
- `CRpcLoop` → `OrientLoop`
- `CRpcTask` → `OrientTask`
- `CRpcLoopRunner` → `OrientLoopRunner`
- `CRpcLoopHost.RunUntilCancelled` → `OrientLoopHost.RunUntilCancelled` in examples and tests
- `CRpcClientLoopHost.RunUntilCancelled` → `OrientLoopHost.RunUntilCancelled` in tests

Update `CRPC.TestHelper/LoopTestDriver.cs` and `CrpcTestBase.cs` to use `OrientLoop`.

Update `Tests/CRPC.Tests/UnitTest1.cs`: replace `CRpcServerLoop.RunUntilCancelled` with `OrientLoopHost.RunUntilCancelled`.

- [ ] **Step 4: Build solution (compile gate only)**

```bash
dotnet build orient-dotnet.sln
```

Expected: PASS for all projects. Do **not** run the full test suite yet; `CRpcGeneratorTests` and HelloWorld generated files still reference `CRpcTask` until Task 8.

Also update `Tests/CRPC.Tests/OrientLoopThreadBindingTests.cs`: rename `CRpcLoop` → `OrientLoop` and `using CRpc.Async` → `using Orient.Runtime` as part of the Step 3 sweep.

---

## Task 8: Protobuf plugin and regenerate HelloWorld

**Files:**
- Modify: `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcGen.cs`
- Modify: `Example/HelloWorld/Server/HelloworldService.cs`
- Modify: `Example/HelloWorld/Client/GreeterClient.cs` (if generated)

- [ ] **Step 1: Update generator usings**

In `CRpcGen.cs` `GenerateServer`, replace emitted usings:

```csharp
sb.AppendLine("using Orient.Runtime;");
sb.AppendLine("using Orient.Rpc;");
sb.AppendLine("using Orient.Rpc.Codec;");
sb.AppendLine("using Orient.Rpc.Server;");
sb.AppendLine("using Orient.Rpc.Protocol;");
```

In `GenerateClient`:

```csharp
sb.AppendLine("using Orient.Runtime;");
sb.AppendLine("using Orient.Rpc;");
sb.AppendLine("using Orient.Rpc.Codec;");
sb.AppendLine("using Orient.Rpc.Client;");
```

Replace generated type names in emitted code strings:
- `CRpcTask` → `OrientTask`
- `CRpcContext` stays `CRpcContext` (protocol type in `Orient.Rpc.Server`)
- `CRpcMessage` stays `CRpcMessage`

- [ ] **Step 2: Update generator tests**

In `Tests/CRPC.Tests/CRpcGeneratorTests.cs`, update assertions to match renamed generator output:

```csharp
// Before
Assert.Contains("protected CRpcTask<bool> PushServerNoticeAsync", serverFile.Content);
Assert.Contains("protected virtual CRpcTask OnPushServerNoticeAsync", clientFile.Content);

// After
Assert.Contains("protected OrientTask<bool> PushServerNoticeAsync", serverFile.Content);
Assert.Contains("protected virtual OrientTask OnPushServerNoticeAsync", clientFile.Content);
```

If any test inspects emitted usings, replace `using CRpc.Async;` expectations with `using Orient.Runtime;`.

- [ ] **Step 3: Hand-update generated HelloWorld service/client**

Update `Example/HelloWorld/Server/HelloworldService.cs` to match the new generator output. Replace old usings with:

```csharp
using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Protocol;
using Orient.Rpc.Codec;
using Orient.Rpc.Server;

public abstract class GreeterServiceBase : IRpcService
{
    public OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
    // ...
}
```

In that file, replace every `CRpcTask` return type or static call with `OrientTask`.

Update `Example/HelloWorld/Client/GreeterClient.cs` with:

```csharp
using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Codec;
using Orient.Rpc.Client;
```

In that client file, replace every `CRpcTask` return type or static call with `OrientTask`.

- [ ] **Step 4: Run generator tests**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcGeneratorTests"
```

Expected: PASS.

---

## Task 9: Delete monolith and add DataManager compile check

**Files:**
- Delete: `CRpc/` directory (entire tree including `CRPC.csproj`)
- Create: `Orient.DataManager/Orient.DataManager.csproj` (empty stub for dependency verification)

- [ ] **Step 1: Verify nothing references `CRpc/CRPC.csproj`**

```bash
rg "CRPC\\\\CRPC\.csproj|CRpc\\\\CRPC\.csproj" --glob "*.csproj" --glob "*.sln"
```

Expected: no matches.

- [ ] **Step 2: Delete `CRpc/` folder**

Remove the monolith project tree after `Orient.Runtime` and `Orient.Rpc` build cleanly.

- [ ] **Step 3: Create stub `Orient.DataManager`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>Orient.DataManager</AssemblyName>
    <RootNamespace>Orient.DataManager</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Orient.Runtime\Orient.Runtime.csproj" />
  </ItemGroup>
</Project>
```

Add to solution (optional but satisfies split spec verification checklist).

```bash
dotnet build Orient.DataManager/Orient.DataManager.csproj
```

Expected: PASS with no `Orient.Rpc` reference.

- [ ] **Step 4: Confirm no stale namespaces**

```bash
rg "namespace CRpc\.|using CRpc\." --glob "*.cs"
```

Expected: no matches outside `Tool/` historical docs (plugin namespace `CRpcProtobufPlugin` is fine).

---

## Task 10: Documentation updates

**Files:**
- Modify: `Doc/architecture.md`
- Modify: `docs/superpowers/specs/2026-06-27-crpc-server-lifecycle-design.md` — add superseded banner for registry sections
- Modify: `docs/superpowers/specs/2026-06-28-a2-rpc-service-registry-design.md` — add implementation note after work is complete
- Modify: `docs/superpowers/specs/2026-06-28-orient-runtime-rpc-split-design.md` — add implementation note after work is complete

- [ ] **Step 1: Update `architecture.md`**

Replace references to:
- `CRpcLoop.RegisterService` / `TryGetService` → `CRpcServer.Services` / `RpcServiceRegistry`
- `CRpcLoop` business runtime → `OrientLoop` in `Orient.Runtime`
- Monolithic `CRPC` → `Orient.Runtime` + `Orient.Rpc`

- [ ] **Step 2: Mark lifecycle spec superseded (registry only)**

At top of `2026-06-27-crpc-server-lifecycle-design.md`, add:

```markdown
**Superseded (registry):** Service registry decisions replaced by `docs/superpowers/specs/2026-06-28-a2-rpc-service-registry-design.md`. `StartAsync` / `StopAsync` / transport-only stop remain in force.
```

Update canonical host pattern in that doc to `server.Services.Register`.

- [ ] **Step 3: Add implementation notes to the 2026-06-28 specs**

Do not mark the specs `Approved` as part of implementation. Approval is a user/process decision before execution. After implementation and verification pass, add this line near the top of both 2026-06-28 specs:

```markdown
**Implementation:** Completed by `docs/superpowers/plans/2026-06-28-a2-runtime-rpc-split.md`.
```

---

## Task 11: Final verification

- [ ] **Step 1: Full test suite**

```bash
dotnet test orient-dotnet.sln
```

Expected: all tests PASS.

- [ ] **Step 2: Build all examples**

```bash
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj
dotnet build Example/HelloWorld/Client/HelloWorldClient.csproj
dotnet build Example/GateWay/GateWayServer/GateWayServer.csproj
dotnet build Example/GateWay/Client/GateWayClient.csproj
```

Expected: PASS.

- [ ] **Step 3: Manual smoke (optional)**

```bash
dotnet run --project Example/HelloWorld/Server/HelloWorldServer.csproj
```

In another terminal, run HelloWorld client against port 7999.

- [ ] **Step 4: Verification checklist (from specs)**

- [ ] `Orient.Runtime` has no DotNetty/Protobuf/Nacos package references
- [ ] `Orient.Rpc` references only `Orient.Runtime` + external packages
- [ ] Monolithic `CRPC` project removed
- [ ] No `CRpc.Async` namespace in `.cs` sources
- [ ] No `loop.RegisterService` in production or test code
- [ ] `RpcServiceRegistryTests` passes
- [ ] `Orient.DataManager` stub compiles against `Orient.Runtime` only

---

## Spec Coverage Self-Review

| Spec requirement | Task |
| --- | --- |
| A2: `RpcServiceRegistry` on loop thread | Task 1 |
| A2: `CRpcServer.Services` | Task 2 |
| A2: remove loop registry | Task 3 |
| A2: handler dispatch `Services.TryGet` | Task 2 |
| A2: examples + tests migration | Tasks 3, 7 |
| Loop binding tests preserved | Task 1 Step 5, Task 7 Step 3 |
| A2: `StopAsync` does not clear registry | Existing `CRpcServerTests` updated in Task 3 |
| Split: atomic batch Tasks 4–7 | Prerequisites + Tasks 4–7 header |
| Split: `Orient.Runtime` BCL-only | Task 4 |
| Split: `Orient.Rpc` with packages + `InternalsVisibleTo` | Task 6 |
| Split: type rename `OrientLoop`/`OrientTask` | Tasks 4, 6, 7 |
| Split: `OrientLoopHost` merge | Task 5 |
| Split: `CRpcLoopHost` aliases | Task 6 |
| Split: consumer ProjectReference updates | Task 7 |
| Split: protobuf plugin usings | Task 8 |
| Split: delete monolith | Task 9 |
| Split: `Orient.DataManager` can reference Runtime only | Task 9 |
| Docs: architecture-draft + lifecycle superseded | Task 10 |

**Placeholder scan:** No TBD/TODO steps in this plan.

**Type consistency:** `RpcServiceRegistry` uses `OrientLoop` after Task 6; `Services.Register` / `TryGet` API stable throughout.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-28-a2-runtime-rpc-split.md`.

**Two execution options:**

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration. Use `superpowers:subagent-driven-development`.

2. **Inline Execution** — execute tasks in this session with checkpoints. Use `superpowers:executing-plans`.

Which approach?
