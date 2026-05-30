# LordUnion Client Cleanup Symmetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move LordUnion cleanup behind `LordUnionSessionClient` so pre-signup and post-game cleanup use the same per-account client boundary as login, signup, and enter-match stages.

**Architecture:** Keep `AccountCleanupFlow` as the focused cleanup engine, but call it through `LordUnionSessionClient.CleanupAsync`. `ThreePlayersOneGameScenario` remains the multi-account orchestrator, no longer owns `AccountCleanupFlow`, and records post-game cleanup outcomes in the scenario report.

**Tech Stack:** C# 12 / .NET 8, `CRpcTask`, `CRpcLoop`, xUnit, existing LordUnion fake transport and protocol helpers.

**Important:** Do not commit automatically. Commit checkpoints in this plan are manual review points only; create a git commit only if the user explicitly asks.

---

## File Structure

- Modify `Tests/LordUnion.IntegrationTests/Sessions/LordUnionSessionClient.cs`: add `AccountCleanupFlow` collaborator and `CleanupAsync`.
- Modify `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`: call cleanup through `bundle.Client`, remove scenario ownership of `AccountCleanupFlow`, preserve post-game best-effort behavior.
- Modify `Tests/LordUnion.IntegrationTests/Reporting/ScenarioReport.cs`: add post-cleanup summaries to the report model.
- Create `Tests/LordUnion.IntegrationTests/Reporting/AccountCleanupSummary.cs`: compact report shape for per-account cleanup results.
- Modify `Tests/LordUnion.IntegrationTests/Reporting/ReportWriter.cs`: include post-cleanup summaries in JSON and optionally a concise console line.
- Modify `Tests/CRPC.Tests/LordUnion/LordUnionSessionClientTests.cs`: add client cleanup facade tests.
- Modify `Tests/CRPC.Tests/LordUnion/ThreePlayersOneGameScenarioTests.cs`: assert scenario invokes client-level cleanup and reports post-cleanup summaries.
- Modify `Tests/CRPC.Tests/LordUnion/ReportWriterTests.cs`: assert JSON includes post-cleanup summaries.
- Keep `Tests/LordUnion.IntegrationTests/Flows/AccountCleanupFlow.cs`: cleanup engine remains separate.
- Keep `Tests/CRPC.Tests/LordUnion/AccountCleanupFlowTests.cs`: engine tests remain valid.

## Task 1: Add Cleanup Facade To `LordUnionSessionClient`

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Sessions/LordUnionSessionClient.cs`
- Test: `Tests/CRPC.Tests/LordUnion/LordUnionSessionClientTests.cs`

- [ ] **Step 1: Write the failing pre-signup cleanup facade test**

Add this test to `LordUnionSessionClientTests`:

```csharp
[Fact]
public void CleanupAsync_PreSignup_SendsUnsignupThroughClientTransport()
{
    const uint userId = 214291552;

    var loop = new CRpcLoop();
    var session = new AccountSession(loop, "player1", codec)
    {
        UserId = userId,
        Nickname = "player-one",
    };
    var transport = new FakeGameServerTransport();
    var client = new LordUnionSessionClient(session, transport, codec);

    transport.OnPacketSentAsync = (packet, packetLoop) =>
    {
        var sent = transport.DecodeSentPacket(
            packet,
            new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });

        if (sent.Kind == ProtocolMessageKind.TourneyUnsignupReq)
        {
            transport.DeliverIncomingMessage(CreateTourneyUnsignupAck(
                tourneyId: 159740,
                matchPoint: 2008280,
                param: 0));
        }

        return CRpcTask.CompletedTask(packetLoop);
    };

    var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
    {
        await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));
        session.SetState(AccountSessionState.LoggedIn);

        return await client.CleanupAsync(CreateMatchConfig(), AccountCleanupRunOptions.PreSignup(0));
    });

    Assert.True(result.UnsignupSent);
    Assert.True(result.UnsignupAckReceived);
    Assert.Equal(0u, result.UnsignupParam);
    Assert.Equal(AccountSessionState.LoggedIn, session.State);
}
```

Add helpers if they are not already present in the file:

```csharp
private static MatchConfig CreateMatchConfig() =>
    new()
    {
        GameId = 1001,
        ProductId = 2008280,
        TourneyId = 159740,
    };

private static ProtocolMessage CreateTourneyUnsignupAck(uint tourneyId, uint matchPoint, uint param) =>
    new()
    {
        Kind = ProtocolMessageKind.TourneyUnsignupAck,
        Acknowledgement = new TKMobileAckMsg
        {
            LobbyAckMsg = new LobbyAckMsg
            {
                TourneyunsignupAckMsg = new TourneyUnsignupAck
                {
                    Tourneyid = tourneyId,
                    Matchpoint = matchPoint,
                    Param = param,
                },
            },
        },
    };
```

- [ ] **Step 2: Write the failing post-game cleanup facade test**

Add this test to `LordUnionSessionClientTests`:

```csharp
[Fact]
public void CleanupAsync_PostGame_AllowsFinishedStateAndKnownMatchId()
{
    const uint matchId = 475051269;

    var loop = new CRpcLoop();
    var session = new AccountSession(loop, "player1", codec)
    {
        UserId = 214291552,
        MatchId = matchId,
    };
    var transport = new FakeGameServerTransport();
    var client = new LordUnionSessionClient(session, transport, codec);

    transport.OnPacketSentAsync = (packet, packetLoop) =>
    {
        var sent = transport.DecodeSentPacket(
            packet,
            new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });

        switch (sent.Kind)
        {
            case ProtocolMessageKind.TourneyUnsignupReq:
                transport.DeliverIncomingMessage(CreateTourneyUnsignupAck(
                    tourneyId: 159740,
                    matchPoint: 2008280,
                    param: 0));
                break;
            case ProtocolMessageKind.ExitGameReq:
                transport.DeliverIncomingMessage(CreateExitGameAck(matchId));
                break;
        }

        return CRpcTask.CompletedTask(packetLoop);
    };

    var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
    {
        await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));
        session.SetState(AccountSessionState.Finished);

        return await client.CleanupAsync(
            CreateMatchConfig(),
            AccountCleanupRunOptions.PostGameCleanup(matchId));
    });

    Assert.Contains(matchId, result.DiscoveredMatchIds);
    Assert.Contains(matchId, result.ExitGameAttemptedMatchIds);
    Assert.Contains(matchId, result.ExitMatchAttemptedMatchIds);
}
```

Add `CreateExitGameAck` if needed:

```csharp
private static ProtocolMessage CreateExitGameAck(uint matchId) =>
    new()
    {
        Kind = ProtocolMessageKind.ExitGameAck,
        Acknowledgement = new TKMobileAckMsg
        {
            MatchAckMsg = new MatchAckMsg
            {
                Matchid = matchId,
                ExitgameAckMsg = new ExitGameAck(),
            },
        },
    };
```

- [ ] **Step 3: Run tests and confirm failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~LordUnionSessionClientTests"
```

Expected: fail because `LordUnionSessionClient.CleanupAsync` does not exist.

- [ ] **Step 4: Add cleanup collaborator and constructor injection**

Modify `LordUnionSessionClient`:

```csharp
private readonly AccountCleanupFlow cleanupFlow;
```

Change the constructor signature:

```csharp
public LordUnionSessionClient(
    AccountSession session,
    IGameServerTransport transport,
    ServerProtocolCodec codec,
    EnterMatchFlow? enterMatchFlow = null,
    AccountCleanupFlow? cleanupFlow = null)
{
    this.session = session ?? throw new ArgumentNullException(nameof(session));
    this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
    this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
    this.enterMatchFlow = enterMatchFlow ?? new EnterMatchFlow(codec);
    this.cleanupFlow = cleanupFlow ?? new AccountCleanupFlow(codec);
}
```

- [ ] **Step 5: Implement `CleanupAsync`**

Add near the other public stage APIs:

```csharp
public CRpcTask<AccountCleanupFlowResult> CleanupAsync(
    MatchConfig match,
    AccountCleanupRunOptions? options = null)
{
    EnsureOnLoopThread();
    ArgumentNullException.ThrowIfNull(match);

    return cleanupFlow.RunAsync(
        session,
        match,
        transport,
        options ?? AccountCleanupRunOptions.PreSignup());
}
```

- [ ] **Step 6: Re-run client tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~LordUnionSessionClientTests"
```

Expected: all `LordUnionSessionClientTests` pass.

## Task 2: Move Scenario Cleanup Calls To Client

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`
- Test: `Tests/CRPC.Tests/LordUnion/ThreePlayersOneGameScenarioTests.cs`

- [ ] **Step 1: Update pre-signup cleanup call**

Replace the current pre-signup cleanup phase:

```csharp
await RunPhaseConcurrentOnLoopAsync(
    bundles,
    bundle => RunAccountCleanupAsync(bundle, config, cancellationToken),
    static (_, _) => { });
```

with:

```csharp
await RunPhaseConcurrentOnLoopAsync(
    bundles,
    bundle => bundle.Client.CleanupAsync(config.Match),
    static (_, _) => { });
```

- [ ] **Step 2: Replace post-game cleanup helper to use client**

Keep best-effort behavior, but call through `bundle.Client`:

```csharp
private async CRpcTask<AccountCleanupSummary> RunPostAccountCleanupAsync(
    AccountBundle bundle,
    LordUnionTestConfig config,
    uint? matchId,
    CancellationToken cancellationToken)
{
    _ = cancellationToken;

    var knownMatchIds = new List<uint>();
    if (matchId is uint resolvedMatchId and > 0)
    {
        knownMatchIds.Add(resolvedMatchId);
    }

    if (bundle.Session.MatchId is uint sessionMatchId and > 0)
    {
        knownMatchIds.Add(sessionMatchId);
    }

    try
    {
        var result = await bundle.Client.CleanupAsync(
            config.Match,
            AccountCleanupRunOptions.PostGameCleanup(knownMatchIds.Distinct().ToArray()));

        return AccountCleanupSummary.FromResult(bundle.Session.Alias, result, errorMessage: null);
    }
    catch (Exception ex)
    {
        return AccountCleanupSummary.FromResult(bundle.Session.Alias, result: null, ex.Message);
    }
}
```

This step depends on Task 3's `AccountCleanupSummary`. If implementing Task 2 first, temporarily keep the existing nullable return shape, then update it in Task 3.

- [ ] **Step 3: Delete `RunAccountCleanupAsync`**

Remove the old private helper that called `accountCleanupFlow.RunAsync(...)` directly.

- [ ] **Step 4: Remove scenario `AccountCleanupFlow` field and constructor parameter**

Remove:

```csharp
private readonly AccountCleanupFlow accountCleanupFlow;
```

Remove the constructor parameter:

```csharp
AccountCleanupFlow? accountCleanupFlow = null,
```

Remove the assignment:

```csharp
this.accountCleanupFlow = accountCleanupFlow ?? new AccountCleanupFlow(this.codec);
```

- [ ] **Step 5: Remove stored `EnterMatchFlow` field if it is only pass-through**

Replace the field:

```csharp
private readonly EnterMatchFlow enterMatchFlow;
```

with either:

```csharp
private readonly EnterMatchFlow? enterMatchFlow;
```

or remove the field entirely and introduce a client factory. The lower-risk option is to keep the nullable field for constructor injection:

```csharp
private readonly EnterMatchFlow? enterMatchFlow;
```

Constructor assignment:

```csharp
this.enterMatchFlow = enterMatchFlow;
```

`CreateBundle` remains:

```csharp
var client = new LordUnionSessionClient(session, transport, codec, enterMatchFlow);
```

This removes eager `new EnterMatchFlow(...)` ownership from the scenario while preserving test injection.

- [ ] **Step 6: Run scenario tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~ThreePlayersOneGameScenarioTests"
```

Expected: all scenario tests pass.

## Task 3: Report Post-Cleanup Outcomes

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Reporting/AccountCleanupSummary.cs`
- Modify: `Tests/LordUnion.IntegrationTests/Reporting/ScenarioReport.cs`
- Modify: `Tests/LordUnion.IntegrationTests/Reporting/ReportWriter.cs`
- Test: `Tests/CRPC.Tests/LordUnion/ReportWriterTests.cs`

- [ ] **Step 1: Add cleanup summary type**

Create `AccountCleanupSummary.cs`:

```csharp
using LordUnion.IntegrationTests.Flows;

namespace LordUnion.IntegrationTests.Reporting;

public sealed class AccountCleanupSummary
{
    public string AccountAlias { get; init; } = string.Empty;

    public bool Completed { get; init; }

    public bool UnsignupSent { get; init; }

    public bool UnsignupAckReceived { get; init; }

    public uint? UnsignupParam { get; init; }

    public IReadOnlyList<uint> DiscoveredMatchIds { get; init; } = Array.Empty<uint>();

    public IReadOnlyList<uint> ExitGameAttemptedMatchIds { get; init; } = Array.Empty<uint>();

    public IReadOnlyList<uint> ExitMatchAttemptedMatchIds { get; init; } = Array.Empty<uint>();

    public string? ErrorMessage { get; init; }

    public static AccountCleanupSummary FromResult(
        string accountAlias,
        AccountCleanupFlowResult? result,
        string? errorMessage)
    {
        return new AccountCleanupSummary
        {
            AccountAlias = accountAlias,
            Completed = result is not null && errorMessage is null,
            UnsignupSent = result?.UnsignupSent ?? false,
            UnsignupAckReceived = result?.UnsignupAckReceived ?? false,
            UnsignupParam = result?.UnsignupParam,
            DiscoveredMatchIds = result?.DiscoveredMatchIds ?? Array.Empty<uint>(),
            ExitGameAttemptedMatchIds = result?.ExitGameAttemptedMatchIds ?? Array.Empty<uint>(),
            ExitMatchAttemptedMatchIds = result?.ExitMatchAttemptedMatchIds ?? Array.Empty<uint>(),
            ErrorMessage = errorMessage,
        };
    }
}
```

- [ ] **Step 2: Add summaries to `ScenarioReport`**

Modify `ScenarioReport`:

```csharp
public IReadOnlyList<AccountCleanupSummary> PostGameCleanupSummaries { get; init; } =
    Array.Empty<AccountCleanupSummary>();
```

- [ ] **Step 3: Wire scenario post-cleanup summaries into report**

In `ThreePlayersOneGameScenario`, capture post-cleanup results before returning success:

```csharp
var postGameCleanupSummaries = Array.Empty<AccountCleanupSummary>();

if (!options.SkipAccountCleanup)
{
    postGameCleanupSummaries = (await RunPhaseConcurrentOnLoopAsync(
        bundles,
        bundle => RunPostAccountCleanupAsync(
            bundle,
            config,
            referenceEnter.MatchId,
            cancellationToken),
        static (_, _) => { }))
        .Select(result => result.Result ?? AccountCleanupSummary.FromResult(
            result.Bundle.Session.Alias,
            result: null,
            result.Exception?.Message ?? "Post-game cleanup did not return a result."))
        .ToArray();
}
```

Set it on the success report:

```csharp
PostGameCleanupSummaries = postGameCleanupSummaries,
```

- [ ] **Step 4: Extend JSON DTO in `ReportWriter`**

Add `PostGameCleanupSummaries` to `ScenarioReportJson`:

```csharp
public List<AccountCleanupSummaryJson> PostGameCleanupSummaries { get; init; } = [];
```

Add mapping in `ToJsonDto`:

```csharp
PostGameCleanupSummaries = report.PostGameCleanupSummaries
    .Select(summary => new AccountCleanupSummaryJson
    {
        AccountAlias = summary.AccountAlias,
        Completed = summary.Completed,
        UnsignupSent = summary.UnsignupSent,
        UnsignupAckReceived = summary.UnsignupAckReceived,
        UnsignupParam = summary.UnsignupParam,
        DiscoveredMatchIds = summary.DiscoveredMatchIds.ToList(),
        ExitGameAttemptedMatchIds = summary.ExitGameAttemptedMatchIds.ToList(),
        ExitMatchAttemptedMatchIds = summary.ExitMatchAttemptedMatchIds.ToList(),
        ErrorMessage = summary.ErrorMessage,
    })
    .ToList(),
```

Add DTO:

```csharp
private sealed class AccountCleanupSummaryJson
{
    public string AccountAlias { get; init; } = string.Empty;

    public bool Completed { get; init; }

    public bool UnsignupSent { get; init; }

    public bool UnsignupAckReceived { get; init; }

    public uint? UnsignupParam { get; init; }

    public List<uint> DiscoveredMatchIds { get; init; } = [];

    public List<uint> ExitGameAttemptedMatchIds { get; init; } = [];

    public List<uint> ExitMatchAttemptedMatchIds { get; init; } = [];

    public string? ErrorMessage { get; init; }
}
```

- [ ] **Step 5: Add compact console line**

In `WriteCompactSuccessSummary`, after `gameEnd`, add:

```csharp
if (report.PostGameCleanupSummaries.Count > 0)
{
    Console.WriteLine($"cleanup {FormatCleanupSummaries(report.PostGameCleanupSummaries)}");
}
```

Add formatter:

```csharp
private static string FormatCleanupSummaries(IReadOnlyList<AccountCleanupSummary> summaries) =>
    string.Join(
        " ",
        summaries.Select(summary =>
            $"{summary.AccountAlias}:completed={summary.Completed}/unsignup={FormatOptionalUInt(summary.UnsignupParam)}"));

private static string FormatOptionalUInt(uint? value) =>
    value.HasValue ? value.Value.ToString() : "(unknown)";
```

If `ReportWriter` already has a helper for optional win seats, reuse it rather than adding a duplicate.

- [ ] **Step 6: Update report writer test**

In `ReportWriterTests.CreateSuccessReport`, add:

```csharp
PostGameCleanupSummaries =
[
    new AccountCleanupSummary
    {
        AccountAlias = "player1",
        Completed = true,
        UnsignupSent = true,
        UnsignupAckReceived = true,
        UnsignupParam = 0,
        DiscoveredMatchIds = [900001],
        ExitGameAttemptedMatchIds = [900001],
        ExitMatchAttemptedMatchIds = [900001],
    },
],
```

In `WriteConsoleSummary_DoesNotThrow_ForSuccessReport`, assert:

```csharp
Assert.Contains("cleanup player1:completed=True/unsignup=0", output, StringComparison.Ordinal);
```

In JSON report test, assert:

```csharp
var cleanup = root.GetProperty("postGameCleanupSummaries");
Assert.Single(cleanup.EnumerateArray());
Assert.True(cleanup[0].GetProperty("completed").GetBoolean());
Assert.Equal(0u, cleanup[0].GetProperty("unsignupParam").GetUInt32());
```

- [ ] **Step 7: Run report tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~ReportWriterTests"
```

Expected: all report writer tests pass.

## Task 4: Verify Whole LordUnion Test Slice

**Files:**
- No new files.

- [ ] **Step 1: Run all LordUnion unit tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~LordUnion"
```

Expected: all LordUnion tests pass.

- [ ] **Step 2: Check lints for edited files**

Use the IDE lints for:

- `Tests/LordUnion.IntegrationTests/Sessions/LordUnionSessionClient.cs`
- `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`
- `Tests/LordUnion.IntegrationTests/Reporting/ScenarioReport.cs`
- `Tests/LordUnion.IntegrationTests/Reporting/ReportWriter.cs`
- `Tests/LordUnion.IntegrationTests/Reporting/AccountCleanupSummary.cs`
- edited test files

Expected: no new diagnostics from this change.

- [ ] **Step 3: Optional live validation**

Only run live validation if requested by the user:

```bash
dotnet run --project Tests/LordUnion.IntegrationTests -- --live --config Tests/LordUnion.IntegrationTests/appsettings.local.json
```

Expected: scenario succeeds. For stronger validation, run a second live pass immediately after the first and confirm no `mobile.param=6` / match-start timeout regression.

## Self-Review Checklist

- [ ] `ThreePlayersOneGameScenario` no longer owns `AccountCleanupFlow`.
- [ ] All cleanup calls in scenario go through `bundle.Client.CleanupAsync`.
- [ ] Post-game cleanup remains best-effort.
- [ ] Post-game cleanup results are visible in JSON.
- [ ] `EnterMatchFlow` implementation is not inlined into `LordUnionSessionClient`.
- [ ] `GameFlow` remains scenario-level.
- [ ] Existing `AccountCleanupFlowTests` still test cleanup engine behavior.
- [ ] No automatic commit was made.
