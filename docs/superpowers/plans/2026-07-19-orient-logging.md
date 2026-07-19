# Orient Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace diagnostic `Console.WriteLine` / `Console.Error.WriteLine` with a project-owned `Orient.Logging` stack: bounded queue, one dedicated consumer thread, auto `ManagedThreadId`, Null-by-default, DotNetty bridged from `Orient.Rpc`.

**Architecture:** New BCL-only `Orient.Logging` project. Producers call `IOrientLogger` → `TryWrite` into `OrientLogService` → single log thread batches to `IOrientLogSink`. `Orient.Runtime` references Logging; DotNetty `InternalLoggerFactory` bridge lives in `Orient.Rpc`. No Microsoft.Extensions.Logging.

**Tech Stack:** C# / .NET 8, `orient-dotnet.sln`, xUnit, DotNetty 0.7.6

**Spec:** `docs/superpowers/specs/2026-07-19-orient-logging-design.md`

**Commit policy:** This repo does **not** auto-commit. Skip every “Commit” step unless the user explicitly asks to commit.

## Locked implementation defaults (from open details)

| Item | Choice |
| --- | --- |
| Queue capacity | `8192` |
| Batch size | `256` |
| Idle flush | drain when woken or after `100ms` wait timeout |
| `OrientLogEvent` | `sealed class` |
| Options property | `IOrientLoggerFactory? LoggerFactory { get; init; }` |
| Unconfigured Runtime fallback | If no logger: keep `Console.Error` for unhandled / Tick-escape only. If logger present: log via logger, no stderr duplicate. |
| Drop summary | Log thread writes summary **directly to sink**, bypassing the queue |
| DotNetty factory tests | Save/restore `InternalLoggerFactory.DefaultFactory` in `try/finally`; mark bridge tests with `[Collection("DotNettyLogging")]` so they do not run in parallel with each other |
| Error/Fatal reserved queue capacity | **Skip in v1** (spec optional); uniform drop when full |
| Spec Lifecycle “Null ⇒ silent unhandled” | **Superseded** by stderr last-resort for unhandled / Tick-escape only |

---

## File structure

### Create — `Orient.Logging/`

| File | Responsibility |
| --- | --- |
| `Orient.Logging/Orient.Logging.csproj` | BCL-only class library, net8.0 |
| `Orient.Logging/OrientLogLevel.cs` | Trace…Fatal |
| `Orient.Logging/OrientLogEvent.cs` | Immutable event payload |
| `Orient.Logging/IOrientLogger.cs` | Call-site frontend |
| `Orient.Logging/IOrientLoggerFactory.cs` | Category factory |
| `Orient.Logging/NullOrientLogger.cs` | Silent logger + factory |
| `Orient.Logging/IOrientLogSink.cs` | Batch write + flush |
| `Orient.Logging/ConsoleOrientLogSink.cs` | Host Console formatter |
| `Orient.Logging/OrientLogService.cs` | Queue + dedicated thread + factory |
| `Orient.Logging/OrientLogger.cs` | Concrete logger that enqueues |
| `Orient.Logging/OrientLoggerExtensions.cs` | Level helpers; hot paths use `IsEnabled` before `$"..."` (v1 skips custom InterpolatedStringHandler) |

### Modify — Runtime

| File | Change |
| --- | --- |
| `Orient.Runtime/Orient.Runtime.csproj` | ProjectReference → `Orient.Logging` |
| `Orient.Runtime/Executor/OrientExecutorOptions.cs` | `LoggerFactory` |
| `Orient.Runtime/Executor/OrientExecutor.cs` | Log unhandled via logger; stderr only if null |
| `Orient.Runtime/Executor/OrientExecutorHost.cs` | Tick-escape via logger/stderr rule |
| `Orient.Runtime/Executor/OrientExecutorRunner.cs` | Same |

### Modify — Rpc

| File | Change |
| --- | --- |
| `Orient.Rpc/Orient.Rpc.csproj` | Reference Logging; remove MEL Console package |
| `Orient.Rpc/Server/CRpcServerOptions.cs` | `LoggerFactory` |
| `Orient.Rpc/Client/CRpcClientOptions.cs` | `LoggerFactory` |
| `Orient.Rpc/Transport/TcpChannelHostOptions.cs` | `ChannelLoggingEnabled` (default false) |
| `Orient.Rpc/Transport/TcpChannelHost.cs` | Conditionally add `LoggingHandler` |
| `Orient.Rpc/Logging/OrientInternalLoggerFactory.cs` | **Create** DotNetty bridge |
| `Orient.Rpc/Logging/OrientInternalLogger.cs` | **Create** |
| `Orient.Rpc/Client/CRpcClient.cs` | Replace Console diagnostics |
| `Orient.Rpc/Server/CRpcServer.cs` | Replace Console diagnostics |
| `Orient.Rpc/Server/CRpcServerHandler.cs` | Replace Console diagnostics |
| `Orient.Rpc/Server/CRpcServerWriteBufferWarningHandler.cs` | Replace Console |
| `Orient.Rpc/Codec/CRpcMessageDecoder.cs` | Replace Console |

### Modify — Examples / Gateway / Docs / Solution

| File | Change |
| --- | --- |
| `orient-dotnet.sln` | Add `Orient.Logging` |
| `Example/HelloWorld/Server/Program.cs` | Create/stop `OrientLogService`, Console sink, wire factories + DotNetty bridge |
| `Example/HelloWorld/Client/Program.cs` | Same pattern |
| `Example/HelloWorld/Client/GreeterClient.cs` | Logger for push display |
| `Example/GateWay/GateWayServer/Program.cs` | Same |
| `Example/GateWay/Client/Program.cs` | Same |
| `Example/GateWay/GateWay.Core/*` | Replace diagnostic Console |
| `Doc/architecture.md` | §8.6 |
| `Tests/Orient.Tests/Logging/*` | **Create** unit tests |

Keep: `Console.CancelKeyPress`, `Console.ReadKey`, `Tool/orient-crpc-plugin` stdio.

---

### Task 1: Scaffold `Orient.Logging` + Null logger

**Files:**
- Create: `Orient.Logging/Orient.Logging.csproj`
- Create: `Orient.Logging/OrientLogLevel.cs`
- Create: `Orient.Logging/OrientLogEvent.cs`
- Create: `Orient.Logging/IOrientLogger.cs`
- Create: `Orient.Logging/IOrientLoggerFactory.cs`
- Create: `Orient.Logging/NullOrientLogger.cs`
- Modify: `orient-dotnet.sln`
- Modify: `Orient.Runtime/Orient.Runtime.csproj` (add ProjectReference)
- Test: `Tests/Orient.Tests/Logging/NullOrientLoggerTests.cs`

- [ ] **Step 1: Create project and add to solution**

```xml
<!-- Orient.Logging/Orient.Logging.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>Orient.Logging</AssemblyName>
    <RootNamespace>Orient.Logging</RootNamespace>
  </PropertyGroup>
</Project>
```

```powershell
dotnet sln orient-dotnet.sln add Orient.Logging/Orient.Logging.csproj
```

Add to `Orient.Runtime.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Orient.Logging\Orient.Logging.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Write failing Null logger test**

```csharp
// Tests/Orient.Tests/Logging/NullOrientLoggerTests.cs
using Orient.Logging;

namespace Orient.Tests.Logging;

public sealed class NullOrientLoggerTests
{
    [Fact]
    public void Null_factory_returns_logger_that_does_not_throw()
    {
        var factory = NullOrientLoggerFactory.Instance;
        var logger = factory.CreateLogger("test");
        Assert.False(logger.IsEnabled(OrientLogLevel.Trace));
        Assert.False(logger.IsEnabled(OrientLogLevel.Error));
        logger.Log(OrientLogLevel.Error, 0, "msg", null);
    }
}
```

- [ ] **Step 3: Run test — expect compile/fail**

```powershell
dotnet test Tests/Orient.Tests/Orient.Tests.csproj --filter FullyQualifiedName~NullOrientLoggerTests
```

Expected: FAIL (types missing)

- [ ] **Step 4: Implement core types + Null**

```csharp
namespace Orient.Logging;

public enum OrientLogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5,
}
```

```csharp
namespace Orient.Logging;

public sealed class OrientLogEvent
{
    public OrientLogEvent(
        DateTimeOffset timestamp,
        OrientLogLevel level,
        int eventId,
        string category,
        int managedThreadId,
        string message,
        Exception? exception)
    {
        Timestamp = timestamp;
        Level = level;
        EventId = eventId;
        Category = category;
        ManagedThreadId = managedThreadId;
        Message = message;
        Exception = exception;
    }

    public DateTimeOffset Timestamp { get; }
    public OrientLogLevel Level { get; }
    public int EventId { get; }
    public string Category { get; }
    public int ManagedThreadId { get; }
    public string Message { get; }
    public Exception? Exception { get; }
}
```

```csharp
namespace Orient.Logging;

public interface IOrientLogger
{
    string Category { get; }
    bool IsEnabled(OrientLogLevel level);
    void Log(OrientLogLevel level, int eventId, string message, Exception? exception = null);
}
```

```csharp
namespace Orient.Logging;

public interface IOrientLoggerFactory
{
    IOrientLogger CreateLogger(string category);
}
```

```csharp
namespace Orient.Logging;

public sealed class NullOrientLogger : IOrientLogger
{
    public static NullOrientLogger Instance { get; } = new();
    private NullOrientLogger() { }
    public string Category => "Null";
    public bool IsEnabled(OrientLogLevel level) => false;
    public void Log(OrientLogLevel level, int eventId, string message, Exception? exception = null) { }
}

public sealed class NullOrientLoggerFactory : IOrientLoggerFactory
{
    public static NullOrientLoggerFactory Instance { get; } = new();
    private NullOrientLoggerFactory() { }
    public IOrientLogger CreateLogger(string category) => NullOrientLogger.Instance;
}
```

- [ ] **Step 5: Run test — expect PASS**

```powershell
dotnet test Tests/Orient.Tests/Orient.Tests.csproj --filter FullyQualifiedName~NullOrientLoggerTests
```

Expected: PASS

---

### Task 2: `OrientLogService` (queue + dedicated thread)

**Files:**
- Create: `Orient.Logging/IOrientLogSink.cs`
- Create: `Orient.Logging/OrientLogger.cs`
- Create: `Orient.Logging/OrientLogService.cs`
- Test: `Tests/Orient.Tests/Logging/OrientLogServiceTests.cs`

- [ ] **Step 1: Write failing service tests**

```csharp
using Orient.Logging;
using System.Collections.Concurrent;

namespace Orient.Tests.Logging;

public sealed class OrientLogServiceTests
{
    private sealed class RecordingSink : IOrientLogSink
    {
        public ConcurrentQueue<OrientLogEvent> Events { get; } = new();
        public ConcurrentQueue<int> WriterThreadIds { get; } = new();

        public void Write(IReadOnlyList<OrientLogEvent> batch)
        {
            WriterThreadIds.Enqueue(Environment.CurrentManagedThreadId);
            foreach (var e in batch)
            {
                Events.Enqueue(e);
            }
        }

        public void Flush() { }
    }

    [Fact]
    public async Task Enqueued_event_is_written_on_log_thread_with_producer_thread_id()
    {
        var sink = new RecordingSink();
        await using var service = new OrientLogService(sink, capacity: 128, batchSize: 16);
        service.Start();
        var logger = service.CreateLogger("cat");
        var producerTid = Environment.CurrentManagedThreadId;

        logger.Log(OrientLogLevel.Info, 1, "hello");

        Assert.True(SpinWait.SpinUntil(() => sink.Events.Count >= 1, TimeSpan.FromSeconds(2)));
        Assert.True(sink.Events.TryDequeue(out var ev));
        Assert.Equal("hello", ev.Message);
        Assert.Equal(producerTid, ev.ManagedThreadId);
        Assert.True(sink.WriterThreadIds.TryPeek(out var writerTid));
        Assert.NotEqual(producerTid, writerTid);
    }

    [Fact]
    public async Task Full_queue_drops_and_does_not_block()
    {
        var sink = new RecordingSink();
        await using var service = new OrientLogService(sink, capacity: 2, batchSize: 1);
        // Do not Start — force queue to fill without drain
        var logger = service.CreateLogger("cat");
        Assert.True(service.TryEnqueueForTests(
            new OrientLogEvent(DateTimeOffset.UtcNow, OrientLogLevel.Info, 0, "c", 1, "a", null)));
        Assert.True(service.TryEnqueueForTests(
            new OrientLogEvent(DateTimeOffset.UtcNow, OrientLogLevel.Info, 0, "c", 1, "b", null)));
        Assert.False(service.TryEnqueueForTests(
            new OrientLogEvent(DateTimeOffset.UtcNow, OrientLogLevel.Info, 0, "c", 1, "c", null)));
        Assert.Equal(1, service.DroppedCount);
    }
}
```

Note: expose `internal` test hooks via `InternalsVisibleTo` for `Orient.Tests`, or make `TryEnqueueForTests` `internal` and add:

```xml
<!-- Orient.Logging.csproj -->
<ItemGroup>
  <InternalsVisibleTo Include="Orient.Tests" />
</ItemGroup>
```

- [ ] **Step 2: Run tests — expect FAIL**

```powershell
dotnet test Tests/Orient.Tests/Orient.Tests.csproj --filter FullyQualifiedName~OrientLogServiceTests
```

- [ ] **Step 3: Implement sink interface + service + logger**

```csharp
namespace Orient.Logging;

public interface IOrientLogSink
{
    void Write(IReadOnlyList<OrientLogEvent> batch);
    void Flush();
}
```

```csharp
namespace Orient.Logging;

public sealed class OrientLogger : IOrientLogger
{
    private readonly OrientLogService service;
    private readonly OrientLogLevel minLevel;

    public OrientLogger(OrientLogService service, string category, OrientLogLevel minLevel)
    {
        this.service = service;
        Category = category;
        this.minLevel = minLevel;
    }

    public string Category { get; }

    public bool IsEnabled(OrientLogLevel level) => level >= minLevel;

    public void Log(OrientLogLevel level, int eventId, string message, Exception? exception = null)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var ev = new OrientLogEvent(
            DateTimeOffset.UtcNow,
            level,
            eventId,
            Category,
            Environment.CurrentManagedThreadId,
            message,
            exception);
        service.TryWrite(ev);
    }
}
```

Implement `OrientLogService` with:

- `ConcurrentQueue<OrientLogEvent>` + `int count` via `Interlocked` for capacity, **or** `Channel<OrientLogEvent>` bounded (`FullMode = DropWrite` is wrong — we need count). Prefer: `Channel.CreateBounded` with `FullMode = Wait` is also wrong. Use:

```csharp
public sealed class OrientLogService : IOrientLoggerFactory, IAsyncDisposable
{
    public const int DefaultCapacity = 8192;
    public const int DefaultBatchSize = 256;

    private readonly IOrientLogSink sink;
    private readonly int capacity;
    private readonly int batchSize;
    private readonly OrientLogLevel minLevel;
    private readonly ConcurrentQueue<OrientLogEvent> queue = new();
    private readonly ManualResetEventSlim signal = new(false);
    private int queued;
    private long dropped;
    private long droppedSinceReport;
    private Thread? thread;
    private volatile bool accepting = true;
    private volatile bool running;

    public OrientLogService(
        IOrientLogSink sink,
        int capacity = DefaultCapacity,
        int batchSize = DefaultBatchSize,
        OrientLogLevel minLevel = OrientLogLevel.Info)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.capacity = capacity;
        this.batchSize = batchSize;
        this.minLevel = minLevel;
    }

    public long DroppedCount => Interlocked.Read(ref dropped);

    public void Start()
    {
        if (thread is not null) return;
        running = true;
        thread = new Thread(ConsumeLoop) { IsBackground = true, Name = "orient-log" };
        thread.Start();
    }

    public IOrientLogger CreateLogger(string category) => new OrientLogger(this, category, minLevel);

    public bool TryWrite(OrientLogEvent ev)
    {
        if (!accepting) return false;
        while (true)
        {
            var current = Volatile.Read(ref queued);
            if (current >= capacity)
            {
                Interlocked.Increment(ref dropped);
                Interlocked.Increment(ref droppedSinceReport);
                return false;
            }
            if (Interlocked.CompareExchange(ref queued, current + 1, current) == current)
            {
                queue.Enqueue(ev);
                signal.Set();
                return true;
            }
        }
    }

    internal bool TryEnqueueForTests(OrientLogEvent ev) => TryWrite(ev);

    private void ConsumeLoop()
    {
        var batch = new List<OrientLogEvent>(batchSize);
        while (running || !queue.IsEmpty)
        {
            batch.Clear();
            while (batch.Count < batchSize && queue.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref queued);
                batch.Add(item);
            }

            if (batch.Count > 0)
            {
                try
                {
                    sink.Write(batch);
                    sink.Flush();
                }
                catch
                {
                    // isolate sink failures; do not crash log thread
                }
            }

            ReportDropsIfNeeded();

            if (batch.Count == 0)
            {
                signal.Reset();
                if (queue.IsEmpty && running)
                {
                    signal.Wait(100);
                }
            }
        }
    }

    private void ReportDropsIfNeeded()
    {
        var n = Interlocked.Exchange(ref droppedSinceReport, 0);
        if (n <= 0) return;
        try
        {
            var summary = new OrientLogEvent(
                DateTimeOffset.UtcNow,
                OrientLogLevel.Warn,
                eventId: 9000,
                category: "Orient.Logging",
                managedThreadId: Environment.CurrentManagedThreadId,
                message: $"Dropped {n} log event(s) due to full queue",
                exception: null);
            sink.Write(new[] { summary });
            sink.Flush();
        }
        catch { }
    }

    public ValueTask DisposeAsync()
    {
        accepting = false;
        running = false;
        signal.Set();
        thread?.Join(TimeSpan.FromSeconds(5));
        return ValueTask.CompletedTask;
    }
}
```

Use `OrientTask` is **not** required inside Logging — this is infrastructure; `IAsyncDisposable` + `Thread.Join` is fine. Do not introduce CRpc/`OrientExecutor` into Logging.

- [ ] **Step 4: Run tests — expect PASS**

```powershell
dotnet test Tests/Orient.Tests/Orient.Tests.csproj --filter FullyQualifiedName~OrientLogServiceTests
```

---

### Task 3: Console sink + level-gated message helper

**Files:**
- Create: `Orient.Logging/ConsoleOrientLogSink.cs`
- Create: `Orient.Logging/OrientLoggerExtensions.cs` (optional convenience + handler)
- Test: `Tests/Orient.Tests/Logging/ConsoleOrientLogSinkTests.cs`

- [ ] **Step 1: Failing format test**

```csharp
[Fact]
public void Formats_timestamp_level_thread_category_message()
{
    var writer = new StringWriter();
    var sink = new ConsoleOrientLogSink(writer);
    var ev = new OrientLogEvent(
        new DateTimeOffset(2026, 7, 19, 15, 26, 10, 123, TimeSpan.Zero),
        OrientLogLevel.Warn,
        1,
        "Orient.Rpc.Client.CRpcClient",
        12,
        "RPC call timed out",
        null);
    sink.Write(new[] { ev });
    var line = writer.ToString();
    Assert.Contains("[WARN]", line);
    Assert.Contains("[T:12]", line);
    Assert.Contains("Orient.Rpc.Client.CRpcClient", line);
    Assert.Contains("RPC call timed out", line);
}
```

- [ ] **Step 2: Implement sink**

```csharp
namespace Orient.Logging;

public sealed class ConsoleOrientLogSink : IOrientLogSink
{
    private readonly TextWriter writer;

    public ConsoleOrientLogSink(TextWriter? writer = null)
    {
        this.writer = writer ?? Console.Out;
    }

    public void Write(IReadOnlyList<OrientLogEvent> batch)
    {
        foreach (var e in batch)
        {
            writer.Write(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            writer.Write(" [");
            writer.Write(LevelName(e.Level));
            writer.Write("] [T:");
            writer.Write(e.ManagedThreadId);
            writer.Write("] ");
            writer.Write(e.Category);
            writer.Write(' ');
            writer.Write(e.Message);
            if (e.Exception is not null)
            {
                writer.Write(" :: ");
                writer.Write(e.Exception);
            }
            writer.WriteLine();
        }
    }

    public void Flush() => writer.Flush();

    private static string LevelName(OrientLogLevel level) => level switch
    {
        OrientLogLevel.Trace => "TRACE",
        OrientLogLevel.Debug => "DEBUG",
        OrientLogLevel.Info => "INFO",
        OrientLogLevel.Warn => "WARN",
        OrientLogLevel.Error => "ERROR",
        OrientLogLevel.Fatal => "FATAL",
        _ => level.ToString().ToUpperInvariant(),
    };
}
```

- [ ] **Step 3: Add extension that skips formatting when disabled**

```csharp
namespace Orient.Logging;

public static class OrientLoggerExtensions
{
    public static void Info(this IOrientLogger logger, int eventId, string message)
        => logger.Log(OrientLogLevel.Info, eventId, message);

    public static void Warn(this IOrientLogger logger, int eventId, string message, Exception? ex = null)
        => logger.Log(OrientLogLevel.Warn, eventId, message, ex);

    public static void Error(this IOrientLogger logger, int eventId, string eventMessage, Exception? ex = null)
        => logger.Log(OrientLogLevel.Error, eventId, eventMessage, ex);

    // Prefer: call sites check IsEnabled before $"..." when message is expensive.
    // Optional later: InterpolatedStringHandler — not required if call sites use IsEnabled.
}
```

For hot paths in Rpc, pattern:

```csharp
if (logger.IsEnabled(OrientLogLevel.Trace))
{
    logger.Log(OrientLogLevel.Trace, 0, $"seq={seq}");
}
```

- [ ] **Step 4: Run sink tests — PASS**

---

### Task 4: Wire `Orient.Runtime`

**Files:**
- Modify: `Orient.Runtime/Executor/OrientExecutorOptions.cs`
- Modify: `Orient.Runtime/Executor/OrientExecutor.cs`
- Modify: `Orient.Runtime/Executor/OrientExecutorHost.cs`
- Modify: `Orient.Runtime/Executor/OrientExecutorRunner.cs`
- Test: `Tests/Orient.Tests/Logging/OrientExecutorLoggingTests.cs`

- [ ] **Step 1: Extend options**

```csharp
using Orient.Logging;

namespace Orient.Runtime;

public sealed class OrientExecutorOptions
{
    public IOrientLoggerFactory? LoggerFactory { get; init; }

    internal Func<IOrientExecutorTimerScheduler>? TimerSchedulerFactory { get; init; }

    internal IOrientExecutorTimerScheduler CreateTimerScheduler()
    {
        return TimerSchedulerFactory?.Invoke() ?? new MinHeapTimerScheduler();
    }

    internal IOrientLogger CreateLogger(string category)
    {
        return (LoggerFactory ?? NullOrientLoggerFactory.Instance).CreateLogger(category);
    }
}
```

- [ ] **Step 2: OrientExecutor holds logger; unhandled path**

In constructor, create `IOrientLogger logger = (options ?? new()).CreateLogger("Orient.Runtime.OrientExecutor");`

Replace `HandleUnhandledException` stderr-when-no-handler body:

```csharp
private void HandleUnhandledException(Exception exception)
{
    var handler = UnhandledException;
    if (handler is null)
    {
        if (logger.IsEnabled(OrientLogLevel.Error))
        {
            logger.Log(OrientLogLevel.Error, eventId: 1001, "OrientExecutor unhandled exception", exception);
        }
        else
        {
            // last-resort when no logger configured (Null disables IsEnabled)
            Console.Error.WriteLine($"OrientExecutor unhandled exception: {exception}");
        }
        return;
    }
    // existing handler try/catch — on handler throw: logger if enabled else Console.Error
}
```

Important: `NullOrientLogger.IsEnabled` is always false, so stderr last-resort still runs when factory unset/Null. When host configures a real factory with Error enabled, stderr is not used.

- [ ] **Step 3: Host / Runner Tick catch**

Same pattern with categories `Orient.Runtime.OrientExecutorHost` / `Orient.Runtime.OrientExecutorRunner` and eventIds `1002` / `1003`. They need a logger — pass via optional parameter or read from `executor` if you expose `IOrientLogger` from executor. Simplest: add `internal IOrientLogger Logger { get; }` on `OrientExecutor` set in ctor.

- [ ] **Step 4: Test stderr last-resort vs logger**

```csharp
using Orient.Logging;
using Orient.Runtime;
using System.Text;

namespace Orient.Tests.Logging;

public sealed class OrientExecutorLoggingTests
{
    [Fact]
    public void Unhandled_with_null_factory_writes_stderr()
    {
        var executor = new OrientExecutor(new OrientExecutorOptions());
        executor.BindToCurrentThread();
        var original = Console.Error;
        var buffer = new StringWriter();
        Console.SetError(buffer);
        try
        {
            executor.Post(() => throw new InvalidOperationException("boom"));
            executor.Tick();
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains("OrientExecutor unhandled exception", buffer.ToString());
        Assert.Contains("boom", buffer.ToString());
    }

    [Fact]
    public async Task Unhandled_with_service_factory_writes_to_sink_not_only_stderr()
    {
        var sink = new ConcurrentBagSink();
        await using var logService = new OrientLogService(sink, capacity: 64, batchSize: 8, minLevel: OrientLogLevel.Error);
        logService.Start();
        var executor = new OrientExecutor(new OrientExecutorOptions { LoggerFactory = logService });
        executor.BindToCurrentThread();
        executor.Post(() => throw new InvalidOperationException("logged"));
        executor.Tick();
        Assert.True(SpinWait.SpinUntil(() => sink.Count >= 1, TimeSpan.FromSeconds(2)));
        Assert.Contains(sink.Snapshot(), e => e.Message.Contains("unhandled", StringComparison.OrdinalIgnoreCase));
    }
}
```

(`ConcurrentBagSink` = test double implementing `IOrientLogSink` with thread-safe list; place next to other Logging tests or inline private nested type.)

Use `Orient.TestHelper` binding patterns if `BindToCurrentThread` + `Tick` already have helpers — prefer those over duplicating DEBUG binding rules.
---

### Task 5: Wire Rpc options + replace Console diagnostics

**Files:**
- Modify: `Orient.Rpc/Orient.Rpc.csproj` — add Logging reference if not transitive-only for bridge; remove:

```xml
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="11.0.0-preview.3.26207.106" />
```

(`Orient.Rpc` already references Runtime which references Logging; explicit Logging reference still needed for bridge types.)

- Modify options + all Console sites listed in File structure
- Introduce small event id constants file:

```csharp
// Orient.Rpc/Logging/OrientRpcLogEventIds.cs
namespace Orient.Rpc.Logging;

internal static class OrientRpcLogEventIds
{
    public const int IgnoredMessageType = 2001;
    public const int UnhandledPush = 2002;
    public const int PushHandlerException = 2003;
    public const int CallTimeout = 2004;
    public const int ProcessException = 2005;
    public const int ClientDisconnected = 2006;
    public const int RemoteDisconnect = 2007;
    public const int ChannelException = 2008;
    public const int DecodeFailed = 2009;
    public const int WriteBufferWarning = 2010;
    public const int ServerStarted = 2011;
}
```

- [ ] **Step 1: Add `LoggerFactory` to `CRpcServerOptions` / `CRpcClientOptions`**

```csharp
public IOrientLoggerFactory? LoggerFactory { get; init; }
```

Resolve in constructors:

```csharp
logger = (options.LoggerFactory ?? NullOrientLoggerFactory.Instance)
    .CreateLogger("Orient.Rpc.Client.CRpcClient");
```

Pass factory into nested types (required plumbing, not optional):

- `CRpcServer` exposes `internal IOrientLogger Logger` (category `Orient.Rpc.Server.CRpcServer`) and passes `LoggerFactory` into `CRpcServerPipelineFactory`.
- `CRpcServerPipelineFactory` constructs `CRpcMessageDecoder` / `CRpcServerWriteBufferWarningHandler` with an `IOrientLogger` from that factory (categories `Orient.Rpc.Codec.CRpcMessageDecoder`, `Orient.Rpc.Server.CRpcServerWriteBufferWarningHandler`).
- `CRpcServerHandler` uses `server.Logger` or a dedicated handler logger from the same factory.
- `CRpcClient` passes `LoggerFactory` into `TcpChannelHostOptions` only if channel logging needs it; client diagnostics use the client logger field.
- Client pipeline decoder: `CRpcClientPipelineFactory` must also take `IOrientLogger` for `CRpcMessageDecoder` (same type as server).

- [ ] **Step 2: Replace each diagnostic Console site**

Mapping guidance:

| Old | New level |
| --- | --- |
| `*********CallAsync send` | delete or Trace behind `IsEnabled` |
| timeout / process exception / decode failed | Warn or Error |
| ignored message type / unhandled push | Warn |
| server started (demo `RunAsync`) | Info |
| disconnect | Info or Warn |

Example:

```csharp
logger.Log(
    OrientLogLevel.Warn,
    OrientRpcLogEventIds.CallTimeout,
    $"CallAsync timeout: {timeout}");
```

- [ ] **Step 3: Build Rpc + run existing tests**

```powershell
dotnet test Tests/Orient.Tests/Orient.Tests.csproj --no-restore
```

Fix any tests that scraped Console output via `ConsoleTestOutput` — update them to inject a recording logger/factory instead where they asserted on Console text.

---

### Task 6: DotNetty bridge + channel logging option

**Files:**
- Create: `Orient.Rpc/Logging/OrientInternalLoggerFactory.cs`
- Create: `Orient.Rpc/Logging/OrientInternalLogger.cs`
- Modify: `Orient.Rpc/Transport/TcpChannelHostOptions.cs`
- Modify: `Orient.Rpc/Transport/TcpChannelHost.cs`
- Test: `Tests/Orient.Tests/Logging/OrientInternalLoggerFactoryTests.cs` with `[Collection("DotNettyLogging")]`

- [ ] **Step 1: Options**

```csharp
public bool ChannelLoggingEnabled { get; init; } = false;
```

In `TcpChannelHost` initializer:

```csharp
if (this.options.ChannelLoggingEnabled)
{
    pipeline.AddLast(new LoggingHandler(this.options.LoggingName));
}
```

v1: default remains `false`. When enabled, stock `LoggingHandler` is acceptable even if it dumps payloads; a quieter Orient-owned handler is a follow-up if needed (spec fallback).
- [ ] **Step 2: Bridge implementation**

```csharp
using DotNetty.Common.Internal.Logging;
using Orient.Logging;

namespace Orient.Rpc.Logging;

public sealed class OrientInternalLoggerFactory : InternalLoggerFactory
{
    private readonly IOrientLoggerFactory loggerFactory;

    public OrientInternalLoggerFactory(IOrientLoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    protected override IInternalLogger NewInstance(string name)
        => new OrientInternalLogger(loggerFactory.CreateLogger(name));
}
```

Implement `OrientInternalLogger : IInternalLogger` forwarding Trace/Debug/Info/Warn/Error to `IOrientLogger` with matching `OrientLogLevel`. For format overloads with args, use `string.Format` only when enabled.

- [ ] **Step 3: Host helper to install factory**

```csharp
// Orient.Rpc/Logging/OrientDotNettyLogging.cs
public static class OrientDotNettyLogging
{
    public static IDisposable Install(IOrientLoggerFactory loggerFactory)
    {
        var previous = InternalLoggerFactory.DefaultFactory;
        InternalLoggerFactory.DefaultFactory = new OrientInternalLoggerFactory(loggerFactory);
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly InternalLoggerFactory previous;
        public Restore(InternalLoggerFactory previous) => this.previous = previous;
        public void Dispose() => InternalLoggerFactory.DefaultFactory = previous;
    }
}
```

- [ ] **Step 4: Tests with save/restore**

```csharp
[Collection("DotNettyLogging")]
public sealed class OrientInternalLoggerFactoryTests
{
    [Fact]
    public void Bridge_enqueues_info()
    {
        var sink = new RecordingSink();
        using var service = /* start OrientLogService */;
        using (OrientDotNettyLogging.Install(service))
        {
            var log = InternalLoggerFactory.GetInstance("test.dotnetty");
            log.Info("ping");
            // wait for sink
        }
    }
}
```

---

### Task 7: Examples + GateWay

**Files:** Example/GateWay Program + Core handlers as listed above.

- [ ] **Step 1: Shared host pattern for each Program**

```csharp
var sink = new ConsoleOrientLogSink();
await using var logService = new OrientLogService(sink, minLevel: OrientLogLevel.Info);
logService.Start();
using var _ = OrientDotNettyLogging.Install(logService);

var executorOptions = new OrientExecutorOptions { LoggerFactory = logService };
var executor = new OrientExecutor(executorOptions);
var serverOptions = new CRpcServerOptions
{
    LoggerFactory = logService,
    // ...
};
```

Replace user-facing `Console.WriteLine("listening...")` with:

```csharp
var hostLog = logService.CreateLogger("HelloWorld.Server");
hostLog.Log(OrientLogLevel.Info, 0, $"CRpc listening on {port}");
```

Keep `Console.CancelKeyPress` / `Console.ReadKey` unchanged.

- [ ] **Step 2: GateWay.Core diagnostic Console → logger via options/factory passed from server**

- [ ] **Step 3: Manual smoke (optional):** run HelloWorld server/client; confirm Info lines appear with `[T:n]`.

---

### Task 8: Docs + solution polish

**Files:**
- Modify: `Doc/architecture.md` §8.6（§10 已移除；待办见 `Doc/TODO.txt`）
- Verify: `orient-dotnet.sln` contains Orient.Logging
- Verify: no remaining diagnostic Console in Runtime/Rpc/GateWay.Core (allowlisted exceptions)

- [x] **Step 1: Rewrite §8.6**

Replace temporary Console observability note with: use `Orient.Logging`; events record `ManagedThreadId`; producers never write sinks directly.

- [x] **Step 2: ~~Rewrite §10 item 4~~（跳过：§10 已不存在）

- [x] **Step 3: Grep gate**

```powershell
rg "Console\.(WriteLine|Error\.WriteLine)" Orient.Runtime Orient.Rpc Example/GateWay/GateWay.Core -g "*.cs"
```

Expected remaining: only intentional last-resort stderr in Runtime unhandled/Tick paths (when Null), or none if those also go through the dual path already implemented. Example Programs should use logger for display lines.

---

### Task 9: Full verification

- [x] **Step 1: Build solution**

```powershell
dotnet build orient-dotnet.sln -c Release
```

Expected: 0 errors

- [x] **Step 2: Run all tests**

```powershell
dotnet test Tests/Orient.Tests/Orient.Tests.csproj -c Release
```

Expected: all PASS

- [x] **Step 3: Spec coverage checklist**

Confirm each spec section has a task:

| Spec | Task |
| --- | --- |
| Orient.Logging project | 1–3 |
| Runtime reference + injection | 4 |
| Rpc migration | 5 |
| DotNetty bridge in Rpc | 6 |
| ChannelLoggingEnabled default false | 6 |
| Examples/Gateway | 7 |
| architecture.md | 8 |
| Remove MEL Console package | 5 |
| Drop summary bypass queue | 2 |
| ThreadId auto | 2 |
| Null default | 1, 4 |
| stderr last-resort | 4 |
| Tests | 1–6, 9 |

---

## Self-review notes (plan author)

- No MEL usage anywhere in tasks.
- No `[executor|io|tp|host]` tags.
- Logging project does not reference Runtime/DotNetty.
- Bridge does not live in Runtime.
- Commit steps omitted per repo policy unless user asks.
- Decoder / write-buffer handlers receive logger via pipeline factory plumbing (Task 5).
- InterpolatedStringHandler deferred; `IsEnabled` + extensions are the v1 equivalent.
- Optional Error reserved capacity skipped in v1.