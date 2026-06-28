# CRpc Reference Client API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide a Dubbo/tRPC-like client reference API so user code can obtain a generated proxy and write `await proxy.MethodAsync(...)` inside a clear `CRpcLoop` runner scope.

**Architecture:** Keep `CRpcClient` as the low-level transport client. Add a thin `CRpcReference` layer that creates/connects `CRpcClient`, injects it into generated proxy classes, and leaves `CRpcLoop` ownership explicit. Add a non-generic `CRpcLoopRunner.RunUntilComplete` overload so examples can use inline `async () => { await client.SayHelloAsync(...); }` without artificial return values.

**Tech Stack:** C#/.NET 8, custom `CRpcTask`, DotNetty transport, xUnit tests.

---

## File Structure

- Modify `CRpc/Async/CRpcLoopRunner.cs`
  - Add `RunUntilComplete(CRpcLoop loop, Func<CRpcTask> operation, int sleepMilliseconds = 1)`.
- Modify `Tests/CRPC.Tests/CRpcLoopRunnerTests.cs`
  - Add coverage for the non-generic overload.
- Create `CRpc/Rpc/CRpc/Client/CRpcReference.cs`
  - Public entry point: `CRpcReference.For<TProxy>()`.
- Create `CRpc/Rpc/CRpc/Client/CRpcReferenceBuilder.cs`
  - Parses `crpc://host:port`, connects a `CRpcClient`, and returns `CRpcReference<TProxy>`.
- Create `CRpc/Rpc/CRpc/Client/CRpcReferenceOfT.cs`
  - Owns the connected `CRpcClient` and generated proxy; disposes transport.
- Create `CRpc/Rpc/CRpc/Client/CRpcProxyActivator.cs`
  - Creates generated proxy instances and injects `IRpcClient` into `__client`.
- Create `Tests/CRPC.Tests/CRpcReferenceTests.cs`
  - Unit tests for proxy activation, URL validation, and builder behavior that does not require a live server.
- Modify `Example/HelloWorld/Client/Program.cs`
  - Use the reference API and inline `await client.SayHelloAsync(...)`.
- Modify `Doc/architecture.md`
  - Update the client API section to describe `CRpcReference` as business-facing and `CRpcClient` as transport-facing.

---

### Task 1: Add Non-Generic Loop Runner Overload

**Files:**
- Modify: `CRpc/Async/CRpcLoopRunner.cs`
- Test: `Tests/CRPC.Tests/CRpcLoopRunnerTests.cs`

- [ ] **Step 1: Add failing test for void-style CRpcTask operation**

Add this test before `RunUntilCompleteRethrowsOperationException`:

```csharp
[Fact]
public void RunUntilCompleteVoidOverloadRunsOperation()
{
    var loop = new CRpcLoop();
    var count = 0;

    CRpcLoopRunner.RunUntilComplete(
        loop,
        async () =>
        {
            await CRpcTask.Delay(1, CRpcLoop.Current);
            count++;
        },
        sleepMilliseconds: 0);

    Assert.Equal(1, count);
}
```

- [ ] **Step 2: Run the focused test and verify it fails to compile**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcLoopRunnerTests" --no-restore
```

Expected: build fails because `RunUntilComplete(CRpcLoop, Func<CRpcTask>, int)` does not exist.

- [ ] **Step 3: Add the overload**

Add this method above the generic overload in `CRpcLoopRunner`:

```csharp
public static void RunUntilComplete(CRpcLoop loop, Func<CRpcTask> operation, int sleepMilliseconds = 1)
{
    RunUntilComplete(
        loop,
        async () =>
        {
            await operation();
            return 0;
        },
        sleepMilliseconds);
}
```

- [ ] **Step 4: Run the focused tests and verify they pass**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcLoopRunnerTests" --no-restore
```

Expected: all `CRpcLoopRunnerTests` pass.

---

### Task 2: Add Proxy Activation Without Networking

**Files:**
- Create: `CRpc/Rpc/CRpc/Client/CRpcProxyActivator.cs`
- Test: `Tests/CRPC.Tests/CRpcReferenceTests.cs`

- [ ] **Step 1: Write tests for generated proxy activation**

Create `Tests/CRPC.Tests/CRpcReferenceTests.cs`:

```csharp
using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;

namespace CRPC.Tests;

public class CRpcReferenceTests
{
    [Fact]
    public void ProxyActivatorInjectsRpcClientIntoGeneratedClientField()
    {
        var rpcClient = new RecordingRpcClient();

        var proxy = CRpcProxyActivator.Create<TestGeneratedClient>(rpcClient);

        Assert.Same(rpcClient, proxy.__client);
    }

    [Fact]
    public void ProxyActivatorRejectsTypeWithoutGeneratedClientField()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CRpcProxyActivator.Create<InvalidGeneratedClient>(new RecordingRpcClient()));

        Assert.Contains("__client", exception.Message);
    }

    private sealed class TestGeneratedClient
    {
        public IRpcClient? __client;
    }

    private sealed class InvalidGeneratedClient
    {
    }

    private sealed class RecordingRpcClient : IRpcClient
    {
        public CRpcTask<CRpcMessage> CallAsync(ushort serviceId, ushort methodId, byte[] body, int timeout)
        {
            throw new NotSupportedException();
        }
    }
}
```

- [ ] **Step 2: Run tests and verify they fail to compile**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcReferenceTests" --no-restore
```

Expected: build fails because `CRpcProxyActivator` does not exist.

- [ ] **Step 3: Implement `CRpcProxyActivator`**

Create `CRpc/Rpc/CRpc/Client/CRpcProxyActivator.cs`:

```csharp
using System.Reflection;
using CRpc.Rpc;

namespace CRpc.Rpc.CRpc.Client;

public static class CRpcProxyActivator
{
    public static TProxy Create<TProxy>(IRpcClient rpcClient)
        where TProxy : class, new()
    {
        ArgumentNullException.ThrowIfNull(rpcClient);

        var proxy = new TProxy();
        var field = typeof(TProxy).GetField(
            "__client",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (field is null || !typeof(IRpcClient).IsAssignableFrom(field.FieldType))
        {
            throw new InvalidOperationException(
                $"{typeof(TProxy).FullName} must expose an IRpcClient field named __client.");
        }

        field.SetValue(proxy, rpcClient);
        return proxy;
    }
}
```

- [ ] **Step 4: Run proxy activation tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcReferenceTests" --no-restore
```

Expected: `CRpcReferenceTests` pass.

---

### Task 3: Add Reference Builder and Disposable Reference

**Files:**
- Create: `CRpc/Rpc/CRpc/Client/CRpcReference.cs`
- Create: `CRpc/Rpc/CRpc/Client/CRpcReferenceBuilder.cs`
- Create: `CRpc/Rpc/CRpc/Client/CRpcReferenceOfT.cs`
- Modify: `Tests/CRPC.Tests/CRpcReferenceTests.cs`

- [ ] **Step 1: Add URL validation tests**

Append these tests to `CRpcReferenceTests`:

```csharp
[Fact]
public void ReferenceBuilderRejectsNonCrpcUrl()
{
    var exception = Assert.Throws<InvalidOperationException>(
        () => CRpcReference.For<TestGeneratedClient>().Url("http://127.0.0.1:7999"));

    Assert.Contains("crpc://", exception.Message);
}

[Fact]
public void ReferenceBuilderRejectsUrlWithoutPort()
{
    var exception = Assert.Throws<InvalidOperationException>(
        () => CRpcReference.For<TestGeneratedClient>().Url("crpc://127.0.0.1"));

    Assert.Contains("port", exception.Message);
}
```

- [ ] **Step 2: Run tests and verify they fail to compile**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcReferenceTests" --no-restore
```

Expected: build fails because `CRpcReference` does not exist.

- [ ] **Step 3: Implement reference entry point**

Create `CRpc/Rpc/CRpc/Client/CRpcReference.cs`:

```csharp
namespace CRpc.Rpc.CRpc.Client;

public static class CRpcReference
{
    public static CRpcReferenceBuilder<TProxy> For<TProxy>()
        where TProxy : class, new()
    {
        return new CRpcReferenceBuilder<TProxy>();
    }
}
```

- [ ] **Step 4: Implement disposable reference wrapper**

Create `CRpc/Rpc/CRpc/Client/CRpcReferenceOfT.cs`:

```csharp
namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcReference<TProxy> : IAsyncDisposable
    where TProxy : class
{
    private readonly CRpcClient client;

    internal CRpcReference(TProxy proxy, CRpcClient client)
    {
        Proxy = proxy;
        this.client = client;
    }

    public TProxy Proxy { get; }

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
    }
}
```

- [ ] **Step 5: Implement URL parsing and connection builder**

Create `CRpc/Rpc/CRpc/Client/CRpcReferenceBuilder.cs`:

```csharp
using CRpc.Async;

namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcReferenceBuilder<TProxy>
    where TProxy : class, new()
{
    private Uri? uri;

    public CRpcReferenceBuilder<TProxy> Url(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != "crpc")
        {
            throw new InvalidOperationException("CRpc reference URL must use the crpc:// scheme.");
        }

        if (parsed.Port <= 0)
        {
            throw new InvalidOperationException("CRpc reference URL must include a port.");
        }

        uri = parsed;
        return this;
    }

    public async Task<CRpcReference<TProxy>> ConnectAsync(CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        var target = uri ?? throw new InvalidOperationException("CRpc reference URL is required.");

        var client = new CRpcClient();
        await client.ConnectAsync(target.Host, target.Port).ConfigureAwait(false);

        var proxy = CRpcProxyActivator.Create<TProxy>(client);
        return new CRpcReference<TProxy>(proxy, client);
    }
}
```

- [ ] **Step 6: Run reference tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcReferenceTests" --no-restore
```

Expected: `CRpcReferenceTests` pass.

---

### Task 4: Update HelloWorld Client to Reference API

**Files:**
- Modify: `Example/HelloWorld/Client/Program.cs`
- Build: `Example/HelloWorld/Client/HelloWorldClient.csproj`

- [ ] **Step 1: Replace manual `CRpcClient` wiring**

Change the client program to:

```csharp
// See https://aka.ms/new-console-template for more information

using CRpc.Async;
using CRpc.Rpc.CRpc.Client;
using Example;

Console.WriteLine("Hello, RPC Client!");

var loop = new CRpcLoop();
await using var reference = await CRpcReference
    .For<GreeterClient>()
    .Url("crpc://127.0.0.1:7999")
    .ConnectAsync(loop);

var client = reference.Proxy;

CRpcLoopRunner.RunUntilComplete(loop, async () =>
{
    for (var i = 0; i < 5; i++)
    {
        HelloRequest req = new HelloRequest();
        req.Msg = $"hi, crpc, I am from client, call={i}";
        var (result, helloReply) = await client.SayHelloAsync(req);
        Console.WriteLine($"call={i}, server return: result={result}, response: {helloReply.Msg}");
    }
});

if (!Console.IsInputRedirected)
{
    Console.ReadKey();
}
```

- [ ] **Step 2: Build the HelloWorld client**

Run:

```bash
dotnet build Example/HelloWorld/Client/HelloWorldClient.csproj -v q
```

Expected: build succeeds. Existing nullable warnings may remain.

---

### Task 5: Document the Client API Boundary

**Files:**
- Modify: `Doc/architecture.md`

- [ ] **Step 1: Add client-facing API text to the target architecture section**

In `## 9. 目标架构（建议方向，不是当前实现）`, add a short subsection after `### 9.2 推荐的 API 形态（草稿)`:

```markdown
### 9.3 Client Reference API

业务代码不直接依赖 `CRpcClient.CallAsync(serviceId, methodId, body, timeout)`。推荐通过 `CRpcReference` 获取生成代理：

```csharp
var loop = new CRpcLoop();
await using var reference = await CRpcReference
    .For<GreeterClient>()
    .Url("crpc://127.0.0.1:7999")
    .ConnectAsync(loop);

var greeter = reference.Proxy;

CRpcLoopRunner.RunUntilComplete(loop, async () =>
{
    var (code, reply) = await greeter.SayHelloAsync(req);
});
```

`CRpcReference` 是业务入口；`CRpcClient` 是底层 transport client，仍负责连接、pending call、request sequence、超时和响应分发。Service 内部调用其它进程时也使用生成代理，但不能创建或驱动新的 loop，必须复用当前 `CRpcLoop.Current`。
```

Then renumber existing `### 9.3 关键不变量（重申）` to `### 9.4 关键不变量（重申）`.

- [ ] **Step 2: Check document headings**

Run:

```bash
rg "^## |^### " Doc/architecture.md
```

Expected: headings are sequential and no duplicate `9.3` remains.

---

### Task 6: Full Verification

**Files:**
- No source changes in this task.

- [ ] **Step 1: Run the unit test suite**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --no-restore
```

Expected: all tests pass.

- [ ] **Step 2: Build both HelloWorld projects**

Run:

```bash
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj -v q
dotnet build Example/HelloWorld/Client/HelloWorldClient.csproj -v q
```

Expected: both builds succeed. Existing nullable/package warnings may remain.

- [ ] **Step 3: Manual smoke test**

Start the server from `Example/HelloWorld/Server/bin/Debug/net8.0`, then run the client from `Example/HelloWorld/Client/bin/Debug/net8.0`.

Expected client output shape:

```text
Hello, RPC Client!
call=0, server return: result=0, response: echo from server, tm=...
call=1, server return: result=0, response: echo from server, tm=...
call=2, server return: result=0, response: echo from server, tm=...
call=3, server return: result=0, response: echo from server, tm=...
call=4, server return: result=0, response: echo from server, tm=...
```

---

## Self-Review

- **Spec coverage:** Covers the requested simplified proxy/reference shape, keeps `CRpcClient` as transport, supports `await client.SayHelloAsync(...)` inside loop runner, and documents Service-internal remote calls.
- **Placeholder scan:** No TODO/TBD placeholders remain.
- **Type consistency:** Plan consistently uses `CRpcReference`, `CRpcReferenceBuilder<TProxy>`, `CRpcReference<TProxy>`, `CRpcProxyActivator`, `CRpcLoopRunner.RunUntilComplete`, `CRpcClient`, and `IRpcClient`.
- **User rule:** No commit step is included; commits must only happen on explicit user request.
