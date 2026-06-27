# CRpc Config Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fail fast on invalid `CRpcServerOptions` / `CRpcClientOptions`, and delete unused `CRpc.Config` stubs so framework transport config has a single surface.

**Architecture:** Extend existing `Validate()` methods on the two options types with numeric range checks and heartbeat-gated cross-field rules. Call `Validate()` at server start and client construction (before DotNetty IO setup). Keep pipeline-factory and `TcpChannelHost` validation as defense in depth. Remove empty `ServerConfig` / `ServiceConfig` files.

**Tech Stack:** C# / .NET, xUnit, DotNetty, `CRpcTask` / `CRpcLoop`.

**User Constraint:** Do not commit or merge automatically. Implementation tasks intentionally omit git commit steps.

---

## Spec Reference

Design spec: `docs/superpowers/specs/2026-06-27-crpc-config-validation-design.md`

**Spec amendment (tests):** Allow `Port == 0` for ephemeral OS-assigned bind. Existing tests in `CRpcServerTests` and `CRpcServerErrorResponseIntegrationTests` rely on `Port = 0`. Validation rule becomes: `Port == 0 || (Port >= 1 && Port <= 65535)`.

## File Structure

| File | Responsibility |
| --- | --- |
| `CRpc/Rpc/CRpc/Server/CRpcServerOptions.cs` | Port/frame/thread/backlog constants + full `Validate()` |
| `CRpc/Rpc/CRpc/Client/CRpcClientOptions.cs` | Frame/timeout/thread constants + full `Validate()` |
| `CRpc/Rpc/CRpc/Server/CRpcServer.cs` | Call `startOptions.Validate()` in `StartInternalAsync` |
| `CRpc/Rpc/CRpc/Client/CRpcClient.cs` | Call `options.Validate()` in `CreateHost` and internal ctor |
| `CRpc/Config/ServerConfig.cs` | **Delete** |
| `CRpc/Config/ServiceConfig.cs` | **Delete** |
| `Tests/CRPC.Tests/CRpcTransportOptionsTests.cs` | Range + boundary + heartbeat-disabled tests |
| `Tests/CRPC.Tests/CRpcServerTests.cs` | Start-time validation behavior test |
| `Doc/TODO.txt` | Archive P0 item 1 when complete |

**Unchanged:** `TcpChannelHostOptions.Validate()`, `CRpcServerPipelineFactory.Configure`, `CRpcClientPipelineFactory.Configure`.

**Task order:** 1 → 2 → 3 → 4 → 5 → 6

---

## Task 1: `CRpcServerOptions.Validate` — transport fields

**Files:**
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServerOptions.cs`
- Modify: `Tests/CRPC.Tests/CRpcTransportOptionsTests.cs`

- [ ] **Step 1: Write failing server validation tests**

Add `using CRpc.Rpc.CRpc.Codec;` to the test file. Append to `CRpcTransportOptionsTests.cs`:

```csharp
[Theory]
[InlineData(65536)]
[InlineData(-1)]
public void CRpcServerOptionsValidateRejectsInvalidPort(int port)
{
    var options = new CRpcServerOptions { Port = port };
    var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    Assert.Equal(nameof(CRpcServerOptions.Port), ex.ParamName);
}

[Fact]
public void CRpcServerOptionsValidateAllowsEphemeralPortZero()
{
    var options = new CRpcServerOptions { Port = 0 };
    options.Validate();
}

[Theory]
[InlineData(0)]
[InlineData(31)]
[InlineData(CRpcServerOptions.MaxMaxFrameLength + 1)]
public void CRpcServerOptionsValidateRejectsInvalidMaxFrameLength(int maxFrameLength)
{
    var options = new CRpcServerOptions { MaxFrameLength = maxFrameLength };
    var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    Assert.Equal(nameof(CRpcServerOptions.MaxFrameLength), ex.ParamName);
}

[Theory]
[InlineData(CRpcMessage.MinFrameLength)]
[InlineData(CRpcServerOptions.MaxMaxFrameLength)]
public void CRpcServerOptionsValidateAcceptsMaxFrameLengthBoundaries(int maxFrameLength)
{
    var options = new CRpcServerOptions { MaxFrameLength = maxFrameLength };
    options.Validate();
}

[Theory]
[InlineData(0, nameof(CRpcServerOptions.BossThreadCount))]
[InlineData(0, nameof(CRpcServerOptions.WorkerThreadCount))]
[InlineData(0, nameof(CRpcServerOptions.SoBacklog))]
public void CRpcServerOptionsValidateRejectsNonPositiveCounts(int value, string paramName)
{
    var options = paramName switch
    {
        nameof(CRpcServerOptions.BossThreadCount) => new CRpcServerOptions { BossThreadCount = value },
        nameof(CRpcServerOptions.WorkerThreadCount) => new CRpcServerOptions { WorkerThreadCount = value },
        nameof(CRpcServerOptions.SoBacklog) => new CRpcServerOptions { SoBacklog = value },
        _ => throw new ArgumentOutOfRangeException(nameof(paramName)),
    };

    var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    Assert.Equal(paramName, ex.ParamName);
}

[Fact]
public void CRpcServerOptionsValidateSkipsReadIdleRulesWhenHeartbeatDisabled()
{
    var options = new CRpcServerOptions
    {
        HeartbeatEnabled = false,
        ReadIdleSeconds = 0,
    };

    options.Validate(clientHeartbeatIntervalSeconds: 15);
}

[Fact]
public void CRpcServerOptionsDefaultsPassValidate()
{
    new CRpcServerOptions().Validate();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcTransportOptionsTests.CRpcServerOptionsValidate" -v q
```

Expected: FAIL — `MaxMaxFrameLength` not defined; validation does not reject invalid values.

- [ ] **Step 3: Implement `CRpcServerOptions.Validate`**

Add `using CRpc.Rpc.CRpc.Codec;` to `CRpcServerOptions.cs`. Add constants and replace `Validate()`:

```csharp
public const int MinPort = 0;

public const int MaxPort = 65535;

public const int MaxMaxFrameLength = 16 * 1024 * 1024;

public void Validate(int clientHeartbeatIntervalSeconds = CRpcClientOptions.DefaultHeartbeatIntervalSeconds)
{
    if (Port != MinPort && (Port < 1 || Port > MaxPort))
    {
        throw new ArgumentOutOfRangeException(
            nameof(Port),
            Port,
            "CRpcServerOptions.Port must be 0 (ephemeral) or between 1 and 65535.");
    }

    if (MaxFrameLength < CRpcMessage.MinFrameLength || MaxFrameLength > MaxMaxFrameLength)
    {
        throw new ArgumentOutOfRangeException(
            nameof(MaxFrameLength),
            MaxFrameLength,
            $"CRpcServerOptions.MaxFrameLength must be between {CRpcMessage.MinFrameLength} and {MaxMaxFrameLength}.");
    }

    if (BossThreadCount <= 0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(BossThreadCount),
            BossThreadCount,
            "CRpcServerOptions.BossThreadCount must be positive.");
    }

    if (WorkerThreadCount <= 0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(WorkerThreadCount),
            WorkerThreadCount,
            "CRpcServerOptions.WorkerThreadCount must be positive.");
    }

    if (SoBacklog <= 0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(SoBacklog),
            SoBacklog,
            "CRpcServerOptions.SoBacklog must be positive.");
    }

    if (!HeartbeatEnabled)
    {
        return;
    }

    if (ReadIdleSeconds <= 0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(ReadIdleSeconds),
            ReadIdleSeconds,
            "CRpcServerOptions.ReadIdleSeconds must be positive when heartbeat is enabled.");
    }

    if (ReadIdleSeconds < clientHeartbeatIntervalSeconds * 2)
    {
        throw new ArgumentOutOfRangeException(
            nameof(ReadIdleSeconds),
            ReadIdleSeconds,
            "CRpcServerOptions.ReadIdleSeconds must be at least twice the client heartbeat interval.");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcTransportOptionsTests.CRpcServerOptionsValidate" -v q
```

Expected: PASS (including existing `CRpcServerOptionsValidateRequiresReadIdleAtLeastTwiceClientInterval`).

---

## Task 2: `CRpcClientOptions.Validate` — transport fields

**Files:**
- Modify: `CRpc/Rpc/CRpc/Client/CRpcClientOptions.cs`
- Modify: `Tests/CRPC.Tests/CRpcTransportOptionsTests.cs`

- [ ] **Step 1: Write failing client validation tests**

Append to `CRpcTransportOptionsTests.cs`:

```csharp
[Theory]
[InlineData(0)]
[InlineData(31)]
[InlineData(CRpcClientOptions.MaxMaxFrameLength + 1)]
public void CRpcClientOptionsValidateRejectsInvalidMaxFrameLength(int maxFrameLength)
{
    var options = new CRpcClientOptions { MaxFrameLength = maxFrameLength };
    var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    Assert.Equal(nameof(CRpcClientOptions.MaxFrameLength), ex.ParamName);
}

[Theory]
[InlineData(0, nameof(CRpcClientOptions.IoThreadCount))]
[InlineData(0, nameof(CRpcClientOptions.ConnectTimeoutSeconds))]
[InlineData(0, nameof(CRpcClientOptions.CallTimeoutMilliseconds))]
public void CRpcClientOptionsValidateRejectsNonPositiveTimeouts(int value, string paramName)
{
    var options = paramName switch
    {
        nameof(CRpcClientOptions.IoThreadCount) => new CRpcClientOptions { IoThreadCount = value },
        nameof(CRpcClientOptions.ConnectTimeoutSeconds) => new CRpcClientOptions { ConnectTimeoutSeconds = value },
        nameof(CRpcClientOptions.CallTimeoutMilliseconds) => new CRpcClientOptions { CallTimeoutMilliseconds = value },
        _ => throw new ArgumentOutOfRangeException(nameof(paramName)),
    };

    var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    Assert.Equal(paramName, ex.ParamName);
}

[Fact]
public void CRpcClientOptionsValidateSkipsHeartbeatIntervalWhenHeartbeatDisabled()
{
    var options = new CRpcClientOptions
    {
        HeartbeatEnabled = false,
        HeartbeatIntervalSeconds = 0,
    };

    options.Validate();
}

[Fact]
public void CRpcClientOptionsDefaultsPassValidate()
{
    new CRpcClientOptions().Validate();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcTransportOptionsTests.CRpcClientOptionsValidate" -v q
```

Expected: FAIL — new reject tests fail; `SkipsHeartbeatIntervalWhenHeartbeatDisabled` fails because current code rejects interval 0 even when disabled.

- [ ] **Step 3: Implement `CRpcClientOptions.Validate`**

Add `using CRpc.Rpc.CRpc.Codec;`. Add constant and replace `Validate()`:

```csharp
public const int MaxMaxFrameLength = 16 * 1024 * 1024;

public void Validate()
{
    if (IoThreadCount <= 0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(IoThreadCount),
            IoThreadCount,
            "CRpcClientOptions.IoThreadCount must be positive.");
    }

    if (ConnectTimeoutSeconds <= 0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(ConnectTimeoutSeconds),
            ConnectTimeoutSeconds,
            "CRpcClientOptions.ConnectTimeoutSeconds must be positive.");
    }

    if (MaxFrameLength < CRpcMessage.MinFrameLength || MaxFrameLength > MaxMaxFrameLength)
    {
        throw new ArgumentOutOfRangeException(
            nameof(MaxFrameLength),
            MaxFrameLength,
            $"CRpcClientOptions.MaxFrameLength must be between {CRpcMessage.MinFrameLength} and {MaxMaxFrameLength}.");
    }

    if (CallTimeoutMilliseconds <= 0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(CallTimeoutMilliseconds),
            CallTimeoutMilliseconds,
            "CRpcClientOptions.CallTimeoutMilliseconds must be positive.");
    }

    if (!HeartbeatEnabled)
    {
        return;
    }

    if (HeartbeatIntervalSeconds <= 0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(HeartbeatIntervalSeconds),
            HeartbeatIntervalSeconds,
            "CRpcClientOptions.HeartbeatIntervalSeconds must be positive when heartbeat is enabled.");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcTransportOptionsTests" -v q
```

Expected: PASS — all transport options tests green.

---

## Task 3: Entry-point validation wiring

**Files:**
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServer.cs:109-118`
- Modify: `CRpc/Rpc/CRpc/Client/CRpcClient.cs:28-43,178-190`

- [ ] **Step 1: Write failing entry-point tests**

Add to `CRpcTransportOptionsTests.cs`:

```csharp
[Fact]
public void CRpcClientConstructorRejectsInvalidOptionsBeforeConnect()
{
    var loop = new CRpc.Async.CRpcLoop();
    var options = new CRpcClientOptions { IoThreadCount = 0 };

    var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new CRpcClient(loop, options));
    Assert.Equal(nameof(CRpcClientOptions.IoThreadCount), ex.ParamName);
}
```

Add to `CRpcServerTests.cs` (inherits `CrpcTestBase`):

```csharp
[Fact]
public void StartAsyncRejectsInvalidPortBeforeBind()
{
    var loop = new CRpcLoop();
    var server = new CRpcServer(loop, new CRpcServerOptions
    {
        Address = IPAddress.Loopback,
        Port = 65536,
    });

    CRpcLoopRunner.RunUntilComplete(loop, () =>
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            CRpcTask _ = server.StartAsync();
        });
        Assert.Equal(nameof(CRpcServerOptions.Port), ex.ParamName);
        Assert.False(server.IsRunning);
    });
}
```

Add `using CRpc.Async;` to `CRpcTransportOptionsTests.cs` if not present.

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcClientConstructorRejectsInvalidOptionsBeforeConnect|FullyQualifiedName~StartAsyncRejectsInvalidPortBeforeBind" -v q
```

Expected: FAIL — invalid options reach DotNetty or no exception thrown.

- [ ] **Step 3: Wire validation at entry points**

In `CRpcServer.StartInternalAsync`, after `cancellationToken.ThrowIfCancellationRequested();`:

```csharp
startOptions.Validate();
```

In `CRpcClient.CreateHost`, first line:

```csharp
options.Validate();
```

In internal constructor `CRpcClient(CRpcLoop loop, CRpcClientOptions options, TcpChannelHost host)`, after null checks:

```csharp
options.Validate();
```

- [ ] **Step 4: Run entry-point tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcClientConstructorRejectsInvalidOptionsBeforeConnect|FullyQualifiedName~StartAsyncRejectsInvalidPortBeforeBind" -v q
```

Expected: PASS

- [ ] **Step 5: Run server lifecycle regression**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcServerTests" -v q
```

Expected: PASS — `Port = 0` ephemeral tests still work.

---

## Task 4: Delete `CRpc.Config` stubs

**Files:**
- Delete: `CRpc/Config/ServerConfig.cs`
- Delete: `CRpc/Config/ServiceConfig.cs`

- [ ] **Step 1: Delete stub files**

Remove both files. Remove the empty `CRpc/Config/` directory if no other files remain.

- [ ] **Step 2: Verify no references remain**

Run:

```bash
rg "CRpc\.Config\.(ServerConfig|ServiceConfig)" --glob "*.cs"
```

Expected: no matches in production code (archival plan docs may still mention the old name — OK).

- [ ] **Step 3: Build solution**

Run:

```bash
dotnet build CRpc/CRpc.csproj -v q
```

Expected: Build succeeded.

---

## Task 5: Full regression

**Files:** (none — verification only)

- [ ] **Step 1: Run full CRPC test project**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj -v q
```

Expected: all tests PASS.

- [ ] **Step 2: Run pipeline factory tests explicitly**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~PipelineFactory" -v q
```

Expected: PASS — defense-in-depth `Validate()` in factories still compatible.

---

## Task 6: Update TODO

**Files:**
- Modify: `Doc/TODO.txt`

- [ ] **Step 1: Archive completed P0 item**

Move P0 item 1 ("补齐配置校验与配置类型收敛") to the archive section at the bottom of `Doc/TODO.txt`, matching the style of other archived entries. Leave remaining P0 items numbered sequentially.

- [ ] **Step 2: Update spec status (optional)**

In `docs/superpowers/specs/2026-06-27-crpc-config-validation-design.md`, change `**Status:** Draft` to `**Status:** Implemented` and note the Port 0 amendment in a one-line **Amendments** subsection if not already documented.

---

## Self-Review Checklist

| Spec requirement | Task |
| --- | --- |
| Server port/frame/thread/backlog validation | Task 1 |
| Client io/connect/frame/call-timeout validation | Task 2 |
| Heartbeat-gated cross-field rules | Tasks 1–2 |
| `StartInternalAsync` fail-fast | Task 3 |
| `CRpcClient` ctor fail-fast | Task 3 |
| Pipeline / TcpChannelHost defense in depth | Unchanged (Task 5 verifies) |
| Delete `ServerConfig` / `ServiceConfig` | Task 4 |
| Unit + integration tests | Tasks 1–3, 5 |
| TODO archive | Task 6 |

**Port 0 amendment:** Documented in plan header; Task 1 implements ephemeral bind support.
