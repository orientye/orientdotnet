# CRpc / HTTP Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove HTTP/JSON from `CRpc.dll` and `crpc-protobuf-plugin`; keep CRpc binary + `IRpcService` only; demonstrate application-owned HTTP routing (and optional single-port Port Unification) in HelloWorld.

**Architecture:** Delete `HttpServer*`, `IRpcHttpJsonCodec`, and codegen HTTP parsers. Regenerate `GreeterServiceBase : IRpcService` only. HelloWorld adds `GreeterHttpHandler` that parses `POST /api/greeter/say-hello`, `loop.Post`s typed `SayHelloAsync`, and writes JSON — without calling `OnMessageAsync`. Optionally `UnifiedServer` sniffs `'CRPC'` vs HTTP on one port.

**Tech Stack:** C# / .NET 8, DotNetty 0.7.6, Google.Protobuf JSON, xUnit, `dotnet test`.

**Spec reference:** `docs/superpowers/specs/2026-06-20-crpc-http-separation-design.md`

**Repository rule:** Do not create commits unless the user explicitly requests them.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcGen.cs` | Stop emitting `IRpcHttpJsonCodec` / `TryGetHttpMethodParsers`. |
| `Example/HelloWorld/Server/HelloworldService.cs` | Regenerated; `GreeterServiceBase : IRpcService` only. |
| `CRpc/Rpc/IRpcHttpJsonCodec.cs` | **Delete.** |
| `CRpc/Rpc/CRpc/Server/HttpServer.cs` | **Delete.** |
| `CRpc/Rpc/CRpc/Server/HttpServerHandler.cs` | **Delete.** |
| `CRpc/Rpc/CRpc/Server/HttpServerOptions.cs` | **Delete.** |
| `CRpc/CRPC.csproj` | Remove `DotNetty.Codecs.Http` if unused. |
| `CRpc/Rpc/CRpc/Server/CRpcConnectionRegistry.cs` | Make `Register` / `Unregister` / `TryGetByChannel` **public** for app-layer handlers. |
| `Tests/CRPC.Tests/HttpServerHandlerTests.cs` | **Delete.** |
| `Tests/CRPC.Tests/CRpcServerTests.cs` | Remove `HttpServer` tests. |
| `Tests/CRPC.Tests/CRpcGeneratorTests.cs` | Assert no HTTP codec in generated server. |
| `Example/HelloWorld/Server/Program.cs` | CRpc-only or unified demo startup. |
| `Example/HelloWorld/Server/HellowolrdServiceImpl.cs` | Add public HTTP façade method. |
| `Example/HelloWorld/Server/HelloWorldServer.csproj` | Add DotNetty HTTP packages for app demo. |
| `Example/HelloWorld/Server/Http/GreeterHttpHandler.cs` | App HTTP route + JSON + `loop.Post`. |
| `Example/HelloWorld/Server/Http/UnifiedServer.cs` | Optional single-port bootstrap. |
| `Example/HelloWorld/Server/Http/PortUnificationHandler.cs` | Sniff CRPC vs HTTP; assemble pipeline. |
| `Doc/architecture.md` | Note HTTP removed from core. |
| `docs/superpowers/specs/2026-05-19-multi-endpoint-crpc-http-design.md` | Superseded header. |
| `docs/superpowers/specs/2026-06-19-crpc-binary-codec-design.md` | Note HTTP outside core. |

---

## Task 1: Codegen — Remove HTTP From Generator (TDD)

**Files:**
- Modify: `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcGen.cs`
- Modify: `Tests/CRPC.Tests/CRpcGeneratorTests.cs`

- [ ] **Step 1: Add failing generator assertion**

Add to `Tests/CRPC.Tests/CRpcGeneratorTests.cs`:

```csharp
[Fact]
public void GeneratedServiceBaseDoesNotImplementHttpJsonCodec()
{
    var response = GenerateHelloWorld(includePush: true);

    var serverFile = Assert.Single(response.File, file => file.Name.EndsWith("Service.cs"));
    Assert.Contains("public abstract class GreeterServiceBase : IRpcService", serverFile.Content);
    Assert.DoesNotContain("IRpcHttpJsonCodec", serverFile.Content);
    Assert.DoesNotContain("TryGetHttpMethodParsers", serverFile.Content);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~GeneratedServiceBaseDoesNotImplementHttpJsonCodec" --no-build
```

If the test project was not built, run `dotnet build Tests/CRPC.Tests/CRPC.Tests.csproj` first.

Expected: FAIL — output contains `IRpcHttpJsonCodec`.

- [ ] **Step 3: Update `CRpcGen.GenerateServiceForServer`**

In `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcGen.cs`:

1. Remove the `sbMethodParsers` `StringBuilder` and the first `foreach` loop that only fills `sbMethodParsers` (lines that append `requestParser` / `responseParser`). Keep the loop that fills `sbPushHelpers` only — merge into the push-only loop or keep one loop with `if (IsServerPush) { ... continue; }` and no parser append for RPC methods.

2. Change the service base line:

```csharp
sb.AppendLine($"public abstract class {service.Name}ServiceBase : IRpcService");
```

3. Delete the entire emitted `TryGetHttpMethodParsers` method block (from `public bool TryGetHttpMethodParsers` through its closing `}`).

- [ ] **Step 4: Run generator test**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcGeneratorTests"
```

Expected: PASS.

---

## Task 2: Regenerate HelloWorld Service File

**Files:**
- Modify: `Example/HelloWorld/Server/HelloworldService.cs`

- [ ] **Step 1: Regenerate or hand-edit generated service**

Either run the protobuf plugin for HelloWorld, or manually update `Example/HelloWorld/Server/HelloworldService.cs` to match generator output:

- Line 14: `public abstract class GreeterServiceBase : IRpcService` (remove `, IRpcHttpJsonCodec`)
- Remove lines 30–36 (`TryGetHttpMethodParsers` method entirely)

Resulting shape:

```csharp
public abstract class GreeterServiceBase : IRpcService
{
    public ushort GetServiceId() => 1000;

    public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
    {
        var rpcContext = (CRpcContext)context;
        var rpcReq = (CRpcMessage)req;
        var methodId = rpcReq.MethodId;
        if (methodId == 1) { return this.__OnMessageSayHelloAsync(rpcContext, rpcReq); }
        return CRpcTask.FromResult((-1, Array.Empty<byte>()));
    }

    // ... __OnMessageSayHelloAsync, PushServerPushHelloAsync, SayHelloAsync abstract ...
}
```

- [ ] **Step 2: Build HelloWorld server**

Run:

```bash
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj
```

Expected: May fail until Task 3 removes `HttpServer` from Program — proceed to Task 3 if so.

---

## Task 3: Delete HTTP From CRpc Core

**Files:**
- Delete: `CRpc/Rpc/IRpcHttpJsonCodec.cs`
- Delete: `CRpc/Rpc/CRpc/Server/HttpServer.cs`
- Delete: `CRpc/Rpc/CRpc/Server/HttpServerHandler.cs`
- Delete: `CRpc/Rpc/CRpc/Server/HttpServerOptions.cs`
- Modify: `CRpc/CRPC.csproj`

- [ ] **Step 1: Delete the four files listed above**

- [ ] **Step 2: Remove HTTP package from core csproj**

In `CRpc/CRPC.csproj`, remove:

```xml
<PackageReference Include="DotNetty.Codecs.Http" Version="0.7.6" />
```

- [ ] **Step 3: Build core**

Run:

```bash
dotnet build CRpc/CRPC.csproj
```

Expected: PASS (no references to deleted types in core).

---

## Task 4: Expose Connection Registry For App Handlers

**Files:**
- Modify: `CRpc/Rpc/CRpc/Server/CRpcConnectionRegistry.cs`

Application HTTP handlers (outside `CRpc.dll`) need to register channels and build `CRpcContext`. Change `internal` to `public` on:

- `Register(IChannel channel)`
- `Unregister(IChannel channel)`
- `TryGetByChannel(IChannel channel, out CRpcConnection connection)`

No logic changes — `EnsureOwnerLoopThread()` stays.

- [ ] **Step 1: Apply visibility change**

- [ ] **Step 2: Build core**

Run:

```bash
dotnet build CRpc/CRPC.csproj
```

Expected: PASS.

---

## Task 5: Remove HTTP Tests From Test Project

**Files:**
- Delete: `Tests/CRPC.Tests/HttpServerHandlerTests.cs`
- Modify: `Tests/CRPC.Tests/CRpcServerTests.cs`

- [ ] **Step 1: Delete `HttpServerHandlerTests.cs`**

- [ ] **Step 2: Remove HTTP tests from `CRpcServerTests.cs`**

Delete entire test methods:

- `HttpStartAsyncThrowsWhenNoCRpcLoopIsBound`
- `HttpStartStopRunsOnOwnerLoop`

Keep `StartAsyncThrowsWhenNoCRpcLoopIsBound` and `StartStopRunsOnOwnerLoop`.

- [ ] **Step 3: Run CRpc server tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcServerTests"
```

Expected: PASS (2 tests).

- [ ] **Step 4: Run full test suite**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: PASS (all remaining tests).

---

## Task 6: HelloWorld — CRpc-Only Program (P3)

**Files:**
- Modify: `Example/HelloWorld/Server/Program.cs`

- [ ] **Step 1: Simplify Program to CRpc-only**

Replace HTTP startup with CRpc-only. Target content:

```csharp
using CRpc.Async;
using CRpc.Rpc.CRpc.Server;
using Example;

Console.WriteLine("Hello, RPC Server!");

var loop = new CRpcLoop();
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var crpcPort = ParsePort(args, defaultPort: 7999);

var crpcServer = new CRpcServer(loop, new CRpcServerOptions { Port = crpcPort });

CRpcLoopRunner.RunUntilComplete(loop, async () =>
{
    loop.RegisterService(new HelloworldServiceImpl());
    await crpcServer.StartAsync(cts.Token);
});

Console.WriteLine($"CRpc listening on {crpcPort}");

try
{
    CRpcLoopHost.RunUntilCancelled(loop, cts.Token);
}
finally
{
    CRpcLoopRunner.RunUntilComplete(loop, async () =>
    {
        await crpcServer.StopAsync();
    });
}

static int ParsePort(string[] args, int defaultPort)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var port) && port > 0)
        {
            return port;
        }
    }

    return defaultPort;
}
```

- [ ] **Step 2: Build example**

Run:

```bash
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj
```

Expected: PASS.

---

## Task 7: HelloWorld — HTTP Façade On Service Impl

**Files:**
- Modify: `Example/HelloWorld/Server/HellowolrdServiceImpl.cs`

- [ ] **Step 1: Add public HTTP entry point**

`SayHelloAsync` is `protected` on the generated base. Add a public wrapper for the app HTTP handler:

```csharp
public CRpcTask<(int, HelloReply)> InvokeSayHelloAsync(CRpcContext context, HelloRequest request)
{
    return SayHelloAsync(context, request);
}
```

- [ ] **Step 2: Build**

Run:

```bash
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj
```

Expected: PASS.

---

## Task 8: HelloWorld — App HTTP Handler (P4)

**Files:**
- Modify: `Example/HelloWorld/Server/HelloWorldServer.csproj`
- Create: `Example/HelloWorld/Server/Http/GreeterHttpHandler.cs`

- [ ] **Step 1: Add DotNetty HTTP packages to example csproj**

In `Example/HelloWorld/Server/HelloWorldServer.csproj`, add:

```xml
<ItemGroup>
  <PackageReference Include="DotNetty.Codecs.Http" Version="0.7.6" />
  <PackageReference Include="DotNetty.Transport" Version="0.7.6" />
</ItemGroup>
```

(`CRPC.csproj` already pulls DotNetty transitively via project reference, but explicit HTTP codec package keeps the example self-documenting.)

- [ ] **Step 2: Create `GreeterHttpHandler.cs`**

Create `Example/HelloWorld/Server/Http/GreeterHttpHandler.cs`:

```csharp
using System.Text;
using CRpc.Async;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using Google.Protobuf;

namespace Example.Http;

public sealed class GreeterHttpHandler : SimpleChannelInboundHandler<IFullHttpRequest>
{
    private const string SayHelloPath = "/api/greeter/say-hello";

    private readonly CRpcLoop loop;
    private readonly CRpcConnectionRegistry connections;
    private readonly HelloworldServiceImpl greeter;

    public GreeterHttpHandler(CRpcLoop loop, CRpcConnectionRegistry connections, HelloworldServiceImpl greeter)
    {
        this.loop = loop;
        this.connections = connections;
        this.greeter = greeter;
    }

    public override void ChannelActive(IChannelHandlerContext context)
    {
        loop.Post(() => connections.Register(context.Channel));
        base.ChannelActive(context);
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        loop.Post(() => connections.Unregister(context.Channel));
        base.ChannelInactive(context);
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest request)
    {
        var keepAlive = HttpUtil.IsKeepAlive(request);

        if (!HttpMethod.Post.Equals(request.Method))
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.MethodNotAllowed, """{"error":"method not allowed"}""");
            return;
        }

        if (!string.Equals(NormalizePath(request.Uri), SayHelloPath, StringComparison.Ordinal))
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.NotFound, """{"error":"route not found"}""");
            return;
        }

        if (!IsJsonContentType(request))
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.UnsupportedMediaType, """{"error":"content type must be application/json"}""");
            return;
        }

        HelloRequest helloRequest;
        try
        {
            var json = request.Content.ToString(Encoding.UTF8);
            helloRequest = HelloRequest.Parser.ParseJson(json);
        }
        catch (Exception)
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.BadRequest, """{"error":"invalid json body"}""");
            return;
        }

        loop.Post(() => ProcessOnLoop(ctx, keepAlive, helloRequest));
    }

    private void ProcessOnLoop(IChannelHandlerContext ctx, bool keepAlive, HelloRequest helloRequest)
    {
        if (!connections.TryGetByChannel(ctx.Channel, out var connection))
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.ServiceUnavailable, """{"error":"connection not ready"}""");
            return;
        }

        var rpcContext = new CRpcContext(connection);
        var task = greeter.InvokeSayHelloAsync(rpcContext, helloRequest);
        var awaiter = task.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            Complete(ctx, keepAlive, awaiter);
            return;
        }

        awaiter.OnCompleted(() => Complete(ctx, keepAlive, awaiter));
    }

    private static void Complete(IChannelHandlerContext ctx, bool keepAlive, CRpcTask<(int code, HelloReply reply)>.Awaiter awaiter)
    {
        try
        {
            var (code, reply) = awaiter.GetResult();
            var bodyJson = JsonFormatter.Default.Format(reply);
            var payload = $$"""{"code":{{code}},"body":{{bodyJson}}}""";
            WriteJson(ctx, keepAlive, HttpResponseStatus.OK, payload);
        }
        catch (Exception)
        {
            WriteJson(ctx, keepAlive, HttpResponseStatus.InternalServerError, """{"error":"internal server error"}""");
        }
    }

    private static string NormalizePath(string uri)
    {
        var path = uri;
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        return path.TrimEnd('/');
    }

    private static bool IsJsonContentType(IFullHttpRequest request)
    {
        if (!request.Headers.TryGet(HttpHeaderNames.ContentType, out var contentType))
        {
            return false;
        }

        var mediaType = contentType.ToString().Split(';')[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteJson(IChannelHandlerContext ctx, bool keepAlive, HttpResponseStatus status, string json)
    {
        var response = new DefaultFullHttpResponse(HttpVersion.Http11, status, Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(json)));
        response.Headers.Set(HttpHeaderNames.ContentType, "application/json; charset=utf-8");
        HttpUtil.SetContentLength(response, response.Content.ReadableBytes);
        if (!keepAlive)
        {
            response.Headers.Set(HttpHeaderNames.Connection, HttpHeaderValues.Close);
        }

        ctx.WriteAndFlushAsync(response);
    }
}
```

- [ ] **Step 3: Build**

Run:

```bash
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj
```

Expected: PASS.

---

## Task 9: HelloWorld — Standalone HTTP Server Bootstrap (Optional Dual-Port Demo)

**Files:**
- Create: `Example/HelloWorld/Server/Http/HttpListenServer.cs`
- Modify: `Example/HelloWorld/Server/Program.cs`

Use this if you want HTTP on a **separate port** (8080) while keeping CRpc on 7999 — both sharing the same loop and `crpcServer.Connections`.

- [ ] **Step 1: Create `HttpListenServer.cs`**

```csharp
using System.Net;
using CRpc.Async;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace Example.Http;

public sealed class HttpListenServer
{
    private readonly CRpcLoop loop;
    private readonly CRpcServer crpcServer;
    private readonly HelloworldServiceImpl greeter;
    private readonly int port;
    private IChannel? channel;
    private IEventLoopGroup? bossGroup;
    private IEventLoopGroup? workerGroup;

    public HttpListenServer(CRpcLoop loop, CRpcServer crpcServer, HelloworldServiceImpl greeter, int port)
    {
        this.loop = loop;
        this.crpcServer = crpcServer;
        this.greeter = greeter;
        this.port = port;
    }

    public CRpcTask StartAsync(CancellationToken cancellationToken = default)
    {
        return StartInternalAsync(cancellationToken);
    }

    private async CRpcTask StartInternalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bossGroup = new MultithreadEventLoopGroup(1);
        workerGroup = new MultithreadEventLoopGroup(1);

        var bootstrap = new ServerBootstrap();
        bootstrap.Group(bossGroup, workerGroup);
        bootstrap.Channel<TcpServerSocketChannel>();
        bootstrap.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
        {
            ch.Pipeline.AddLast(new HttpServerCodec());
            ch.Pipeline.AddLast(new HttpObjectAggregator(65536));
            ch.Pipeline.AddLast(new GreeterHttpHandler(loop, crpcServer.Connections, greeter));
        }));

        channel = await CRpcTask.FromTask(bootstrap.BindAsync(IPAddress.Loopback, port), loop);
    }

    public CRpcTask StopAsync()
    {
        return StopInternalAsync();
    }

    private async CRpcTask StopInternalAsync()
    {
        if (channel is not null)
        {
            await CRpcTask.FromTask(channel.CloseAsync(), loop);
            channel = null;
        }

        if (workerGroup is not null)
        {
            await CRpcTask.FromTask(
                workerGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                loop);
            workerGroup = null;
        }

        if (bossGroup is not null)
        {
            await CRpcTask.FromTask(
                bossGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                loop);
            bossGroup = null;
        }
    }
}
```

- [ ] **Step 2: Wire optional HTTP port in `Program.cs`**

After `crpcServer.StartAsync`, also start HTTP demo server on port 8080:

```csharp
var impl = new HelloworldServiceImpl();
loop.RegisterService(impl);

var httpPort = crpcPort == 7999 ? 8080 : crpcPort + 1000;
var httpListen = new Example.Http.HttpListenServer(loop, crpcServer, impl, httpPort);

await crpcServer.StartAsync(cts.Token);
await httpListen.StartAsync(cts.Token);

Console.WriteLine($"CRpc listening on {crpcPort}, HTTP demo on {httpPort}");
Console.WriteLine($"POST http://127.0.0.1:{httpPort}/api/greeter/say-hello");
```

In `finally`, `await httpListen.StopAsync()` before `crpcServer.StopAsync()`.

- [ ] **Step 3: Manual smoke test**

Run the server, then:

```bash
curl -s -X POST http://127.0.0.1:8080/api/greeter/say-hello -H "Content-Type: application/json" -d "{\"msg\":\"world\"}"
```

Expected: JSON with `"code":0` and a `body` containing `Hello world`.

---

## Task 10: HelloWorld — Port Unification (P5)

**Files:**
- Create: `Example/HelloWorld/Server/Http/PortUnificationHandler.cs`
- Create: `Example/HelloWorld/Server/Http/UnifiedServer.cs`
- Modify: `Example/HelloWorld/Server/Program.cs` (optional `--unified` flag)

- [ ] **Step 1: Create `PortUnificationHandler.cs`**

```csharp
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Channels;

namespace Example.Http;

public sealed class PortUnificationHandler : ByteToMessageDecoder
{
    private readonly Action<IChannelHandlerContext> configureCrpc;
    private readonly Action<IChannelHandlerContext> configureHttp;

    public PortUnificationHandler(
        Action<IChannelHandlerContext> configureCrpc,
        Action<IChannelHandlerContext> configureHttp)
    {
        this.configureCrpc = configureCrpc;
        this.configureHttp = configureHttp;
    }

    protected override void Decode(IChannelHandlerContext context, IByteBuffer input, List<object> output)
    {
        if (input.ReadableBytes < 4)
        {
            return;
        }

        input.MarkReaderIndex();
        var magic = input.ReadInt();
        input.ResetReaderIndex();

        if (magic == CRpcMessage.Magic)
        {
            configureCrpc(context);
        }
        else
        {
            configureHttp(context);
        }

        context.Pipeline.Remove(this);
    }
}
```

- [ ] **Step 2: Create `UnifiedServer.cs`**

```csharp
using System.Net;
using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace Example.Http;

public sealed class UnifiedServer
{
    private readonly CRpcLoop loop;
    private readonly CRpcServer crpcServer;
    private readonly HelloworldServiceImpl greeter;
    private readonly int port;
    private readonly int maxFrameLength;
    private IChannel? channel;
    private IEventLoopGroup? bossGroup;
    private IEventLoopGroup? workerGroup;

    public UnifiedServer(
        CRpcLoop loop,
        CRpcServer crpcServer,
        HelloworldServiceImpl greeter,
        int port,
        int maxFrameLength = CRpcServerOptions.DefaultMaxFrameLength)
    {
        this.loop = loop;
        this.crpcServer = crpcServer;
        this.greeter = greeter;
        this.port = port;
        this.maxFrameLength = maxFrameLength;
    }

    public CRpcTask StartAsync(CancellationToken cancellationToken = default)
    {
        return StartInternalAsync(cancellationToken);
    }

    private async CRpcTask StartInternalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bossGroup = new MultithreadEventLoopGroup(1);
        workerGroup = new MultithreadEventLoopGroup(1);

        var bootstrap = new ServerBootstrap();
        bootstrap.Group(bossGroup, workerGroup);
        bootstrap.Channel<TcpServerSocketChannel>();
        bootstrap.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
        {
            ch.Pipeline.AddLast(new PortUnificationHandler(
                ctx =>
                {
                    ctx.Pipeline.AddLast(new CRpcMessageDecoder(maxFrameLength));
                    ctx.Pipeline.AddLast(new CRpcMessageEncoder());
                    ctx.Pipeline.AddLast(new CRpcServerHandler(crpcServer));
                },
                ctx =>
                {
                    ctx.Pipeline.AddLast(new HttpServerCodec());
                    ctx.Pipeline.AddLast(new HttpObjectAggregator(65536));
                    ctx.Pipeline.AddLast(new GreeterHttpHandler(loop, crpcServer.Connections, greeter));
                }));
        }));

        channel = await CRpcTask.FromTask(bootstrap.BindAsync(IPAddress.Loopback, port), loop);
    }

    public CRpcTask StopAsync()
    {
        return StopInternalAsync();
    }

    private async CRpcTask StopInternalAsync()
    {
        if (channel is not null)
        {
            await CRpcTask.FromTask(channel.CloseAsync(), loop);
            channel = null;
        }

        if (workerGroup is not null)
        {
            await CRpcTask.FromTask(
                workerGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                loop);
            workerGroup = null;
        }

        if (bossGroup is not null)
        {
            await CRpcTask.FromTask(
                bossGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                loop);
            bossGroup = null;
        }
    }
}
```

- [ ] **Step 3: Add `--unified` flag to Program.cs**

When `args` contains `--unified`:

- Do **not** call `crpcServer.StartAsync` (UnifiedServer owns the listen socket).
- Create `UnifiedServer` with the same `crpcServer` instance (for `Connections` + `CRpcServerHandler`).
- Print: `Unified CRpc+HTTP on port {port}`.

When `--unified` is absent, keep Task 9 dual-port behavior (or CRpc-only if you prefer minimal default).

- [ ] **Step 4: Manual smoke test (unified)**

```bash
# HTTP on same port
curl -s -X POST http://127.0.0.1:7999/api/greeter/say-hello -H "Content-Type: application/json" -d "{\"msg\":\"unified\"}"
```

Expected: same JSON envelope as Task 9.

(CRpc binary clients still connect to the same port with `CRPC` magic frames.)

---

## Task 11: Documentation Updates (P6)

**Files:**
- Modify: `Doc/architecture.md`
- Modify: `docs/superpowers/specs/2026-05-19-multi-endpoint-crpc-http-design.md`
- Modify: `docs/superpowers/specs/2026-06-19-crpc-binary-codec-design.md`
- Modify: `docs/superpowers/specs/2026-06-20-crpc-http-separation-design.md`

- [ ] **Step 1: Add superseded note to `2026-05-19-multi-endpoint-crpc-http-design.md`**

At top after title:

```markdown
> **Superseded (HTTP-in-core):** `docs/superpowers/specs/2026-06-20-crpc-http-separation-design.md` — `HttpServer` and `IRpcHttpJsonCodec` removed from core.
```

- [ ] **Step 2: Update `2026-06-19-crpc-binary-codec-design.md`**

Replace the line `HTTP handler (HttpServerHandler, IRpcHttpJsonCodec) unchanged.` with:

```markdown
HTTP/JSON is an application concern; core no longer includes `HttpServerHandler` or `IRpcHttpJsonCodec`. Binary codec is unaffected.
```

- [ ] **Step 3: Update `Doc/architecture.md`**

In the endpoint table (~line 17), change HTTP-in-core references to:

```markdown
| 端点 | **CRpc 在核心**；HTTP 由应用层实现（见 `Example/HelloWorld/Server/Http/`），可选 Port Unification 与 CRpc 共端口 |
```

Remove or strike `HttpServer` from core component lists; point to `Example.Http.GreeterHttpHandler`.

- [ ] **Step 4: Set spec status to Approved**

In `docs/superpowers/specs/2026-06-20-crpc-http-separation-design.md`, change:

```markdown
**Status:** Approved
```

---

## Task 12: Final Verification

- [ ] **Step 1: Full test suite**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: all tests PASS.

- [ ] **Step 2: Build solution projects**

```bash
dotnet build CRpc/CRPC.csproj
dotnet build Example/HelloWorld/Server/HelloWorldServer.csproj
dotnet build Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcProtobufPlugin.csproj
```

Expected: PASS.

- [ ] **Step 3: Grep for removed symbols in core**

```bash
rg "IRpcHttpJsonCodec|HttpServerHandler|HttpServerOptions" CRpc/
```

Expected: no matches.

---

## Plan Self-Review

| Spec requirement | Task |
| --- | --- |
| Delete HTTP from core | Task 3 |
| Codegen CRpc only | Task 1–2 |
| No `IRpcHttpJsonCodec` | Task 1, 3 |
| App HTTP routing | Task 8–9 |
| HTTP calls typed method, not `OnMessageAsync` | Task 7–8 (`InvokeSayHelloAsync`) |
| Port Unification in app | Task 10 |
| Same loop / registry | Task 8–10 use `crpcServer.Connections` |
| Connection registry for apps | Task 4 |
| Doc updates | Task 11 |
| Delete HttpServerHandlerTests | Task 5 |

No TBD placeholders. Types (`GreeterHttpHandler`, `UnifiedServer`, `InvokeSayHelloAsync`) consistent across tasks.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-20-crpc-http-separation.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
