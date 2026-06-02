# LordUnion XML Replay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable LordUnion integration tests to replay bid/play from golden XML fixtures when the server sends `LordInitCardAck.testrecordid`, while TKLordSvr (separate track) writes Generate recordings and semantically diffs them against Origin.

**Architecture:** Client track **A** adds `Games/TKLord/` replay modules (`XmlRecordParser`, `XmlRecordCatalog`, `XmlCardCodec`, `XmlReplayBotPolicy`, `XmlReplayCoordinator`) wired through existing `GameFlow` / `IBotPolicy` / `ScenarioRunOptions`. `TestRecordId` flows from regenerated proto → `ClassicLordVariant.DecodeGameEvent` → coordinator validates three-seat agreement and loads fixtures before any replay decision. Server track **B** extends `LocalRecordGenerator` for Cases/Generate/Origin paths and a semantic diff oracle; client does not implement B.

**Tech Stack:** C# 12 / .NET 8, `CRpcTask`, xUnit (`CRPC.Tests`), protobuf-net codegen script, GB2312 XML fixtures; server: C++ VS2017, `TK_INTEGRATION_TEST` macros.

**Spec:** `docs/superpowers/specs/2026-06-01-lordunion-xml-replay-design.md`

**Important:** Do not commit automatically. Commit checkpoints below are manual review points only; create a git commit only if the user explicitly asks.

---

## File Structure

### Client (orientdotnet)

| File | Responsibility |
|------|----------------|
| `Tests/LordUnion.IntegrationTests/Protocol/GenerateLordUnionProto.ps1` | Add optional `-TkLordProtoRoot` override so IT proto includes `testrecordid` |
| `Tests/LordUnion.IntegrationTests/Protocol/Generated/LordUnionProtocol.g.cs` | Regenerated; `LordInitCardAck.Testrecordid` |
| `Tests/LordUnion.IntegrationTests/GameVariants/GameEvent.cs` | `TestRecordId` on init-card events |
| `Tests/LordUnion.IntegrationTests/GameVariants/ClassicLordVariant.cs` | Map `LordInitCardAck.Testrecordid` |
| `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlCardCodec.cs` | XML card string ↔ `byte[]` |
| `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlRecordAction.cs` | Parsed action DTO |
| `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlRecordParser.cs` | GB2312 XML parse, per-seat queues for id 2/10 |
| `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlRecordCatalog.cs` | Load fixture by `testrecordid`, expose `XmlReplayScript` |
| `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlReplayCoordinator.cs` | Three-seat agreement, catalog load, policy factory |
| `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlReplayBotPolicy.cs` | `IBotPolicy` replay implementation |
| `Tests/LordUnion.IntegrationTests/Scenarios/ScenarioRunOptions.cs` | Optional `XmlReplayCoordinator` |
| `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs` | Create coordinator, wire replay scheduler |
| `Tests/LordUnion.IntegrationTests/Reporting/ScenarioFailureDetail.cs` | `TestRecordId`, `FixturePath` on failure |
| `Tests/LordUnion.IntegrationTests/LordUnion.IntegrationTests.csproj` | Copy `Games/TKLord/Cases/*.xml` to output |
| `Tests/CRPC.Tests/LordUnion/XmlCardCodecTests.cs` | Unit tests |
| `Tests/CRPC.Tests/LordUnion/XmlRecordParserTests.cs` | Unit tests against five fixtures |
| `Tests/CRPC.Tests/LordUnion/XmlReplayBotPolicyTests.cs` | Policy unit tests |
| `Tests/CRPC.Tests/LordUnion/XmlReplayCoordinatorTests.cs` | Agreement / missing-fixture tests |
| `Tests/CRPC.Tests/LordUnion/XmlReplayFakeGameFlowTests.cs` | Fake transport game-flow replay |

### Server (TKLordSvr — parallel track)

| File | Responsibility |
|------|----------------|
| `Define/TestDefine.h` | Guard `TK_INTEGRATION_TEST_USE_CASES` requires `TK_INTEGRATION_TEST` |
| `Module/Test/LocalRecordGenerator.cpp` | Cases/Generate/Origin paths; non-Cases → `Record\TKLord\OnlyLordTakeOut\` |
| `Module/Test/RecordCaseReader.cpp` (new) | Sequential Origin read + deck extraction |
| `Module/Test/RecordSemanticDiff.cpp` (new) | B oracle: compare id 2/10 + results |
| `Game/TKLordGame.cpp` | Set `testrecordid` on init ack; invoke B diff after Generate write |
| `Protocol/TKLord.proto` | Keep `testrecordid` field 5 in sync with client proto |

---

## Task 1: Regenerate Proto And Plumb TestRecordId

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Protocol/GenerateLordUnionProto.ps1`
- Modify: `Tests/LordUnion.IntegrationTests/GameVariants/GameEvent.cs`
- Modify: `Tests/LordUnion.IntegrationTests/GameVariants/ClassicLordVariant.cs`
- Regenerate: `Tests/LordUnion.IntegrationTests/Protocol/Generated/LordUnionProtocol.g.cs`
- Test: `Tests/CRPC.Tests/LordUnion/ClassicLordVariantTests.cs` (create if missing, or add to existing variant tests)

- [ ] **Step 1: Write the failing decode test**

Create `Tests/CRPC.Tests/LordUnion/ClassicLordVariantTests.cs`:

```csharp
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace CRPC.Tests.LordUnion;

public sealed class ClassicLordVariantTests
{
    [Fact]
    public void DecodeGameEvent_CardsDealt_IncludesTestRecordId()
    {
        var variant = new ClassicLordVariant();
        var ack = new TKMobileAckMsg
        {
            LordAckMsg = new LordAckMsg
            {
                Matchid = 42,
                LordinitcardAckMsg = new LordInitCardAck
                {
                    Firstcallseat = 1,
                    Cards = new byte[] { 0, 1, 2 },
                    Testrecordid = "20260601_7646425803181457480",
                },
            },
        };

        var gameEvent = variant.DecodeGameEvent(ack);

        Assert.NotNull(gameEvent);
        Assert.Equal(GameEventKind.CardsDealt, gameEvent!.Kind);
        Assert.Equal("20260601_7646425803181457480", gameEvent.TestRecordId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~ClassicLordVariantTests.DecodeGameEvent_CardsDealt_IncludesTestRecordId"
```

Expected: FAIL — `Testrecordid` property or `GameEvent.TestRecordId` missing.

- [ ] **Step 3: Add `-TkLordProtoRoot` to codegen script**

At top of `GenerateLordUnionProto.ps1`, add parameter:

```powershell
[string] $TkLordProtoRoot = ''
```

Replace the `Resolve-ProtoPath $LordUnionProtoRoot 'TKLord.proto'` line in `$protoFiles` with:

```powershell
if ([string]::IsNullOrWhiteSpace($TkLordProtoRoot)) {
    $tkLordProto = Resolve-ProtoPath $LordUnionProtoRoot 'TKLord.proto'
} else {
    $tkLordProto = Resolve-ProtoPath $TkLordProtoRoot 'TKLord.proto'
}
```

Use `$tkLordProto` in the array instead of inline `Resolve-ProtoPath $LordUnionProtoRoot 'TKLord.proto'`.

- [ ] **Step 4: Regenerate protocol**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/LordUnion.IntegrationTests/Protocol/GenerateLordUnionProto.ps1 `
  -TkLordProtoRoot "C:/orient/my/orientdotnet/Tests/LordUnion.IntegrationTests/Games/TKLord/Protocol/Raw"
```

Expected: `Generated ... LordUnionProtocol.g.cs` and `LordInitCardAck` contains `public string? Testrecordid { get; set; }`.

- [ ] **Step 5: Add TestRecordId to GameEvent**

In `GameEvent.cs`, add:

```csharp
public string? TestRecordId { get; init; }
```

- [ ] **Step 6: Map field in ClassicLordVariant**

In `ClassicLordVariant.cs`, inside the `LordinitcardAckMsg` branch:

```csharp
if (lordAck.LordinitcardAckMsg is { } initCardAck)
{
    return new GameEvent
    {
        Kind = GameEventKind.CardsDealt,
        MatchId = matchId,
        FirstCallSeat = initCardAck.Firstcallseat,
        Cards = initCardAck.Cards,
        TestRecordId = initCardAck.Testrecordid,
    };
}
```

- [ ] **Step 7: Run test to verify it passes**

Run the Step 2 command again. Expected: PASS.

- [ ] **Step 8: Commit (manual, only if user asks)**

---

## Task 2: XmlCardCodec

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlCardCodec.cs`
- Test: `Tests/CRPC.Tests/LordUnion/XmlCardCodecTests.cs`

Card strings in recordings use two-character tokens: suit + rank (`D3`, `S3`, `HK`, `CT`) or jokers `GB` / `GL`. Encoding must round-trip with `CardCodec.Encode` / `GameCard(byte)`.

- [ ] **Step 1: Write failing codec tests**

```csharp
using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.Games.TKLord.Replay;

namespace CRPC.Tests.LordUnion;

public sealed class XmlCardCodecTests
{
    [Fact]
    public void DecodePlayString_MatchesCardCodecBytes()
    {
        var bytes = XmlCardCodec.DecodePlayString("D3S3");
        var cards = CardCodec.Decode(bytes);

        Assert.Equal(2, cards.Count);
        Assert.Equal(new GameCard(39), cards[0]); // D3
        Assert.Equal(new GameCard(0), cards[1]);  // S3
    }

    [Fact]
    public void DecodePlayString_SupportsJokers()
    {
        var bytes = XmlCardCodec.DecodePlayString("GBGL");
        var cards = CardCodec.Decode(bytes);

        Assert.Equal(2, cards.Count);
        Assert.Equal(new GameCard(52), cards[0]);
        Assert.Equal(new GameCard(53), cards[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DecodePlayString_EmptyMeansPass(string value)
    {
        Assert.Empty(XmlCardCodec.DecodePlayString(value));
    }
}
```

- [ ] **Step 2: Run test — expect FAIL**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~XmlCardCodecTests"
```

- [ ] **Step 3: Implement XmlCardCodec**

Create `XmlCardCodec.cs`:

```csharp
using LordUnion.IntegrationTests.Bots;

namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public static class XmlCardCodec
{
    private static readonly Dictionary<char, int> SuitToColor = new()
    {
        ['S'] = 0,
        ['H'] = 1,
        ['C'] = 2,
        ['D'] = 3,
    };

    private static readonly Dictionary<char, int> RankToValueOffset = new()
    {
        ['3'] = 0,
        ['4'] = 1,
        ['5'] = 2,
        ['6'] = 3,
        ['7'] = 4,
        ['8'] = 5,
        ['9'] = 6,
        ['T'] = 7,
        ['J'] = 8,
        ['Q'] = 9,
        ['K'] = 10,
        ['A'] = 11,
        ['2'] = 12,
    };

    public static byte[] DecodePlayString(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return Array.Empty<byte>();
        }

        var tokens = encoded.Trim();
        var cards = new List<GameCard>();

        for (var i = 0; i < tokens.Length;)
        {
            if (i + 1 >= tokens.Length)
            {
                throw new FormatException($"Incomplete card token at offset {i} in '{encoded}'.");
            }

            var suit = tokens[i];
            var rank = tokens[i + 1];
            i += 2;

            if (suit == 'G')
            {
                cards.Add(new GameCard(rank switch
                {
                    'B' => 52,
                    'L' => 53,
                    _ => throw new FormatException($"Unknown joker rank '{rank}' in '{encoded}'."),
                }));
                continue;
            }

            if (!SuitToColor.TryGetValue(suit, out var color))
            {
                throw new FormatException($"Unknown suit '{suit}' in '{encoded}'.");
            }

            if (!RankToValueOffset.TryGetValue(rank, out var rankOffset))
            {
                throw new FormatException($"Unknown rank '{rank}' in '{encoded}'.");
            }

            var byteValue = (byte)(color * 13 + rankOffset);
            cards.Add(new GameCard(byteValue));
        }

        return CardCodec.Encode(cards);
    }
}
```

- [ ] **Step 4: Run tests — expect PASS**

- [ ] **Step 5: Commit (manual, only if user asks)**

---

## Task 3: XmlRecordParser

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlRecordAction.cs`
- Create: `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlReplayScript.cs`
- Create: `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlRecordParser.cs`
- Test: `Tests/CRPC.Tests/LordUnion/XmlRecordParserTests.cs`

- [ ] **Step 1: Write failing parser tests**

```csharp
using LordUnion.IntegrationTests.Games.TKLord.Replay;

namespace CRPC.Tests.LordUnion;

public sealed class XmlRecordParserTests
{
    private static string FixturePath(string stem) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "Games",
            "TKLord",
            "Cases",
            $"{stem}.xml"));

    [Theory]
    [InlineData("20260601_7646425803181457480", 1u, 1u, 2u)] // seat1 one bid=2
    [InlineData("20260601_7646425803181457480", 0u, 1u, 3u)] // seat0 one bid=3
    public void Parse_ExtractsBidQueuePerSeat(string stem, uint seat, int bidIndex, uint expectedScore)
    {
        var script = XmlRecordParser.ParseFile(FixturePath(stem));
        var bids = script.BidsBySeat[seat];

        Assert.True(bidIndex < bids.Count);
        Assert.Equal(expectedScore, bids[bidIndex].BidScore);
    }

    [Fact]
    public void Parse_ExtractsPlayActionsForSeat0FirstPlay()
    {
        var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
        var plays = script.PlaysBySeat[0];

        Assert.NotEmpty(plays);
        Assert.Equal("D3S3", plays[0].CardString);
        Assert.False(plays[0].IsPass);
    }

    [Fact]
    public void Parse_AllFiveFixtures_Succeed()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Games", "TKLord", "Cases"));
        foreach (var file in Directory.EnumerateFiles(dir, "*.xml"))
        {
            var script = XmlRecordParser.ParseFile(file);
            Assert.True(script.BidsBySeat.Values.Sum(q => q.Count) >= 3);
            Assert.True(script.PlaysBySeat.Values.Sum(q => q.Count) >= 3);
        }
    }
}
```

- [ ] **Step 2: Add fixture copy to csproj (needed for tests)**

In `LordUnion.IntegrationTests.csproj`:

```xml
<ItemGroup>
  <None Include="Games\TKLord\Cases\*.xml">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

Mirror the same `ItemGroup` in `CRPC.Tests.csproj` (or rely on project reference output copy — prefer explicit copy in `LordUnion.IntegrationTests.csproj` only; CRPC.Tests references that project so fixtures land in `LordUnion.IntegrationTests` output; tests run from `CRPC.Tests` output — **also** add copy to `CRPC.Tests.csproj` pointing at the source fixtures):

```xml
<ItemGroup>
  <None Include="..\LordUnion.IntegrationTests\Games\TKLord\Cases\*.xml">
    <Link>Games\TKLord\Cases\%(Filename)%(Extension)</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 3: Run parser tests — expect FAIL**

- [ ] **Step 4: Implement DTOs and parser**

`XmlRecordAction.cs`:

```csharp
namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public sealed record XmlBidAction(uint BidScore);

public sealed record XmlPlayAction(string CardString, bool IsPass);
```

`XmlReplayScript.cs`:

```csharp
namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public sealed class XmlReplayScript
{
    public required string TestRecordId { get; init; }

    public required IReadOnlyDictionary<uint, IReadOnlyList<XmlBidAction>> BidsBySeat { get; init; }

    public required IReadOnlyDictionary<uint, IReadOnlyList<XmlPlayAction>> PlaysBySeat { get; init; }
}
```

`XmlRecordParser.cs` (core logic):

```csharp
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public static class XmlRecordParser
{
    private static readonly Regex ActionTagPattern = new(
        @"<a\b[^>]*\bid=""(\d+)""[^>]*(?:\bs=""(\d+)""[^>]*)?(?:\bo=""([^""]*)""[^>]*)?[^>]*/?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static XmlReplayScript ParseFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"XML replay fixture not found: {path}", path);
        }

        var bytes = File.ReadAllBytes(path);
        var xml = Encoding.GetEncoding(936).GetString(bytes); // GB2312
        return Parse(xml, Path.GetFileNameWithoutExtension(path));
    }

    public static XmlReplayScript Parse(string xml, string testRecordId)
    {
        var bids = new Dictionary<uint, List<XmlBidAction>>
        {
            [0] = new(),
            [1] = new(),
            [2] = new(),
        };
        var plays = new Dictionary<uint, List<XmlPlayAction>>
        {
            [0] = new(),
            [1] = new(),
            [2] = new(),
        };

        foreach (Match match in ActionTagPattern.Matches(xml))
        {
            var id = int.Parse(match.Groups[1].Value);
            if (id != 2 && id != 10)
            {
                continue;
            }

            if (!match.Groups[2].Success)
            {
                continue;
            }

            var seat = uint.Parse(match.Groups[2].Value);
            var o = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;

            if (id == 2)
            {
                bids[seat].Add(new XmlBidAction(uint.Parse(o)));
            }
            else
            {
                plays[seat].Add(new XmlPlayAction(o, string.IsNullOrEmpty(o)));
            }
        }

        return new XmlReplayScript
        {
            TestRecordId = testRecordId,
            BidsBySeat = bids.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<XmlBidAction>)pair.Value),
            PlaysBySeat = plays.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<XmlPlayAction>)pair.Value),
        };
    }
}
```

- [ ] **Step 5: Run parser tests — expect PASS**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~XmlRecordParserTests"
```

- [ ] **Step 6: Commit (manual, only if user asks)**

---

## Task 4: XmlRecordCatalog

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlRecordCatalog.cs`
- Test: extend `XmlRecordParserTests` or add `XmlRecordCatalogTests.cs`

- [ ] **Step 1: Write failing catalog test**

```csharp
[Fact]
public void Load_ResolvesFixtureByStem()
{
    var catalog = XmlRecordCatalog.Load(
        "20260601_7646425803181457480",
        AppContext.BaseDirectory);

    Assert.Equal("20260601_7646425803181457480", catalog.Script.TestRecordId);
    Assert.True(catalog.FixturePath.EndsWith("20260601_7646425803181457480.xml", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void Load_MissingFixture_ThrowsFileNotFound()
{
    var ex = Assert.Throws<FileNotFoundException>(() =>
        XmlRecordCatalog.Load("missing_case_id", AppContext.BaseDirectory));

    Assert.Contains("missing_case_id", ex.Message, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Implement catalog**

```csharp
namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public sealed record XmlRecordCatalog(XmlReplayScript Script, string FixturePath)
{
    public static XmlRecordCatalog Load(string testRecordId, string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRecordId);

        var fixturePath = Path.GetFullPath(Path.Combine(
            baseDirectory,
            "Games",
            "TKLord",
            "Cases",
            $"{testRecordId}.xml"));

        var script = XmlRecordParser.ParseFile(fixturePath);
        return new XmlRecordCatalog(script, fixturePath);
    }
}
```

- [ ] **Step 3: Run tests — expect PASS**

- [ ] **Step 4: Commit (manual, only if user asks)**

---

## Task 5: XmlReplayCoordinator

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlReplayCoordinator.cs`
- Modify: `Tests/LordUnion.IntegrationTests/Scenarios/ScenarioRunOptions.cs`
- Test: `Tests/CRPC.Tests/LordUnion/XmlReplayCoordinatorTests.cs`

Coordinator runs on the shared `CRpcLoop` thread; state mutations happen synchronously inside `RegisterInitCard`.

- [ ] **Step 1: Write failing coordinator tests**

```csharp
[Fact]
public void RegisterInitCard_AllEmpty_StaysInactive()
{
    var coordinator = new XmlReplayCoordinator(AppContext.BaseDirectory);
    coordinator.RegisterInitCard(0, null);
    coordinator.RegisterInitCard(1, "");
    coordinator.RegisterInitCard(2, "  ");

    Assert.False(coordinator.IsReplayActive);
}

[Fact]
public void RegisterInitCard_Mismatch_Throws()
{
    var coordinator = new XmlReplayCoordinator(AppContext.BaseDirectory);
    coordinator.RegisterInitCard(0, "case_a");

    var ex = Assert.Throws<InvalidOperationException>(() =>
        coordinator.RegisterInitCard(1, "case_b"));

    Assert.Contains("testrecordid mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void RegisterInitCard_LoadsCatalogWhenActive()
{
    var coordinator = new XmlReplayCoordinator(AppContext.BaseDirectory);
    coordinator.RegisterInitCard(0, "20260601_7646425803181457480");
    coordinator.RegisterInitCard(1, "20260601_7646425803181457480");
    coordinator.RegisterInitCard(2, "20260601_7646425803181457480");

    Assert.True(coordinator.IsReplayActive);
    Assert.NotNull(coordinator.Catalog);
}
```

- [ ] **Step 2: Implement coordinator**

```csharp
namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public sealed class XmlReplayCoordinator
{
    private readonly string baseDirectory;
    private readonly Dictionary<uint, string?> idsBySeat = new();
    private string? canonicalId;
    private XmlRecordCatalog? catalog;

    public XmlReplayCoordinator(string baseDirectory)
    {
        this.baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
    }

    public bool IsReplayActive => !string.IsNullOrWhiteSpace(canonicalId);

    public XmlRecordCatalog? Catalog => catalog;

    public string? TestRecordId => canonicalId;

    public string? FixturePath => catalog?.FixturePath;

    public void RegisterInitCard(uint seat, string? testRecordId)
    {
        idsBySeat[seat] = string.IsNullOrWhiteSpace(testRecordId) ? null : testRecordId.Trim();

        if (idsBySeat.Values.Any(id => id is not null))
        {
            var nonEmpty = idsBySeat.Values.Where(id => id is not null).Select(id => id!).Distinct().ToList();
            if (nonEmpty.Count > 1)
            {
                throw new InvalidOperationException(
                    $"LordInitCardAck testrecordid mismatch across seats: {string.Join(", ", idsBySeat.Select(pair => $"{pair.Key}={pair.Value ?? "<empty>"}"))}.");
            }

            canonicalId = nonEmpty[0];
            catalog ??= XmlRecordCatalog.Load(canonicalId, baseDirectory);
        }
    }

    public XmlReplayBotPolicy CreatePolicy(uint seat)
    {
        if (!IsReplayActive || catalog is null)
        {
            return XmlReplayBotPolicy.CreateMinimalFallback(seat);
        }

        return XmlReplayBotPolicy.CreateReplay(catalog.Script, seat);
    }
}
```

Add to `ScenarioRunOptions.cs`:

```csharp
public XmlReplayCoordinator? XmlReplayCoordinator { get; init; }
```

- [ ] **Step 3: Run coordinator tests — expect PASS**

- [ ] **Step 4: Commit (manual, only if user asks)**

---

## Task 6: XmlReplayBotPolicy

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlReplayBotPolicy.cs`
- Test: `Tests/CRPC.Tests/LordUnion/XmlReplayBotPolicyTests.cs`

Mirror event→decision mapping from `MinimalLandlordBotPolicy`, but dequeue recorded actions. When coordinator inactive, delegate to inner `MinimalLandlordBotPolicy`.

- [ ] **Step 1: Write failing policy tests**

```csharp
[Fact]
public void TryDecide_BidRequested_ReplaysRecordedBidScore()
{
    var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
    var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 1);
    policy.SetSeat(1);

    var decision = policy.TryDecide(new BotActionContext(
        new GameEvent
        {
            Kind = GameEventKind.BidRequested,
            MatchId = 1,
            CurCallSeat = 1,
            NextCallSeat = 1,
            CurScore = 0,
            ValidateScore = 0,
        },
        new ProtocolMessage(Array.Empty<byte>(), 0),
        1,
        1));

    Assert.NotNull(decision);
    Assert.Equal(BotDecisionKind.Bid, decision!.Kind);
    Assert.Equal(2u, decision.CurScore);
}

[Fact]
public void TryDecide_PlayRequested_ReplaysRecordedCards()
{
    var script = XmlRecordParser.ParseFile(FixturePath("20260601_7646425803181457480"));
    var policy = XmlReplayBotPolicy.CreateReplay(script, seat: 0);

    // Consume bids for seat 0 first (record has one bid=3)
    policy.TryDecide(BidContextEvent(seat: 0, next: 0));
    // Skip to first play for seat 0 — landlord leads in this fixture after bids
    var playDecision = policy.TryDecide(new BotActionContext(
        new GameEvent
        {
            Kind = GameEventKind.CardsPlayed,
            MatchId = 1,
            Seat = 2,
            NextPlayer = 0,
            PassPlayer = 2,
            Cards = Array.Empty<byte>(),
        },
        new ProtocolMessage(Array.Empty<byte>(), 0),
        1,
        0));

    Assert.NotNull(playDecision);
    Assert.Equal(BotDecisionKind.Play, playDecision!.Kind);
    Assert.Equal(new byte[] { 39, 0 }, playDecision.Cards); // D3S3 sorted
}
```

(Add local helpers `FixturePath`, `BidContextEvent` in the test class.)

- [ ] **Step 2: Implement XmlReplayBotPolicy**

Key fields: per-seat bid/play queue indices; optional `MinimalLandlordBotPolicy fallback`.

```csharp
public sealed class XmlReplayBotPolicy : IBotPolicy
{
    private readonly XmlReplayScript? script;
    private readonly MinimalLandlordBotPolicy fallback;
    private int bidIndex;
    private int playIndex;
    private uint seat;

    public BotGameState State => fallback.State;

    public static XmlReplayBotPolicy CreateReplay(XmlReplayScript script, uint seat) =>
        new(script, seat);

    public static XmlReplayBotPolicy CreateMinimalFallback(uint seat) =>
        new(script: null, seat);

    private XmlReplayBotPolicy(XmlReplayScript? script, uint seat)
    {
        this.script = script;
        fallback = new MinimalLandlordBotPolicy();
        SetSeat(seat);
    }

    public void SetSeat(uint seat)
    {
        this.seat = seat;
        fallback.SetSeat(seat);
    }

    public void ApplyGameEvent(GameEvent gameEvent)
    {
        fallback.ApplyGameEvent(gameEvent);

        if (script is not null && gameEvent.Kind == GameEventKind.CardsDealt)
        {
            // Coordinator handles catalog; policy only replays.
        }
    }

    public BotDecision? TryDecide(BotActionContext context)
    {
        if (script is null)
        {
            return fallback.TryDecide(context);
        }

        var gameEvent = context.Event;
        return gameEvent.Kind switch
        {
            GameEventKind.ReadyRequested => BotDecision.Ready(),
            GameEventKind.CardsDealt when gameEvent.FirstCallSeat == seat => DecideBid(context),
            GameEventKind.BidRequested when gameEvent.NextCallSeat == seat => DecideBid(context),
            GameEventKind.LandlordDeclared when gameEvent.LordSeat == seat => DecidePlay(context),
            GameEventKind.TurnStarted when IsLeadTurn(gameEvent, seat) => DecidePlay(context),
            GameEventKind.CardsPlayed or GameEventKind.PassPlayed when gameEvent.NextPlayer == seat =>
                DecidePlay(context),
            _ => null,
        };
    }

    private BotDecision DecideBid(BotActionContext context)
    {
        var bids = script!.BidsBySeat[seat];
        if (bidIndex >= bids.Count)
        {
            throw new InvalidOperationException(
                $"XML replay exhausted bid actions for seat {seat} in {script.TestRecordId}.");
        }

        var recorded = bids[bidIndex++];
        var evt = context.Event;
        return BotDecision.Bid(
            evt.CurCallSeat ?? seat,
            evt.NextCallSeat ?? seat,
            evt.ValidateScore ?? 0,
            recorded.BidScore);
    }

    private BotDecision DecidePlay(BotActionContext context)
    {
        var plays = script!.PlaysBySeat[seat];
        if (playIndex >= plays.Count)
        {
            throw new InvalidOperationException(
                $"XML replay exhausted play actions for seat {seat} in {script.TestRecordId}.");
        }

        var recorded = plays[playIndex++];
        var evt = context.Event;
        var nextPlayer = evt.NextPlayer ?? seat;
        var passPlayer = evt.PassPlayer ?? (int)seat;

        if (recorded.IsPass)
        {
            return BotDecision.Pass(nextPlayer, passPlayer);
        }

        return BotDecision.Play(nextPlayer, XmlCardCodec.DecodePlayString(recorded.CardString));
    }

    private static bool IsLeadTurn(GameEvent gameEvent, uint seat) =>
        gameEvent.SeatList is { Count: > 0 } seatList && seatList[0] == seat;
}
```

- [ ] **Step 3: Run policy tests — iterate until PASS**

- [ ] **Step 4: Commit (manual, only if user asks)**

---

## Task 7: Wire Scenario And Coordinator Into Game Phase

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`
- Modify: `Tests/LordUnion.IntegrationTests/Games/TKLord/Replay/XmlReplayBotPolicy.cs` (hook coordinator registration)
- Modify: `Tests/LordUnion.IntegrationTests/Reporting/ScenarioFailureDetail.cs`
- Modify: `Tests/LordUnion.IntegrationTests/Reporting/ReportWriter.cs`

- [ ] **Step 1: Register init cards from policy**

Add optional `XmlReplayCoordinator? coordinator` parameter to `XmlReplayBotPolicy.CreateReplay` overload used by scenario, or pass coordinator into policy constructor:

```csharp
public void OnInitCardObserved(string? testRecordId) =>
    coordinator?.RegisterInitCard(seat, testRecordId);
```

Call from `ApplyGameEvent` when `gameEvent.Kind == GameEventKind.CardsDealt`:

```csharp
coordinator?.RegisterInitCard(seat, gameEvent.TestRecordId);
```

- [ ] **Step 2: Update RunGameAsync in ThreePlayersOneGameScenario**

Before game phase in `RunCoreWithBundlesAsync`, create coordinator when not supplied:

```csharp
var xmlReplayCoordinator = options.XmlReplayCoordinator
    ?? new XmlReplayCoordinator(AppContext.BaseDirectory);
var gameOptions = options.XmlReplayCoordinator is null
    ? options with { XmlReplayCoordinator = xmlReplayCoordinator }
    : options;
```

In `RunGameAsync`:

```csharp
private async CRpcTask<GameStageResult> RunGameAsync(
    AccountBundle bundle,
    LordUnionGameProfile profile,
    LordUnionTestConfig config,
    ScenarioRunOptions options,
    CancellationToken cancellationToken)
{
    _ = cancellationToken;

    if (options.PlayGameOverride is not null)
    {
        // unchanged stub path
    }

    if (options.XmlReplayCoordinator is { } coordinator)
    {
        var seat = bundle.Client.Session.SeatOrder;
        var policy = coordinator.CreatePolicy(seat);
        if (policy is XmlReplayBotPolicy replayPolicy)
        {
            replayPolicy.AttachCoordinator(coordinator, seat);
        }

        return await bundle.Client.PlayGameAsync(
            profile,
            policy,
            ImmediateActionScheduler.Instance,
            config.Timeouts.GameOverTimeout);
    }

    return await bundle.Client.PlayGameAsync(profile, config, options);
}
```

Add `using LordUnion.IntegrationTests.Bots.Pacing;` and `using LordUnion.IntegrationTests.Games.TKLord.Replay;`.

- [ ] **Step 3: Enrich failure reports**

In `ScenarioFailureDetail`, add:

```csharp
public string? TestRecordId { get; init; }
public string? FixturePath { get; init; }
```

When try/catch around game phase captures `FileNotFoundException` or coordinator mismatch, populate these fields in `BuildFailureReport`.

Update `ReportWriter` to emit `testRecordId` / `fixturePath` in JSON failure output (add one test in `ReportWriterTests`).

- [ ] **Step 4: Run regression fake scenario**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~ThreePlayersOneGameScenarioTests"
```

Expected: existing three tests PASS (coordinator path uses minimal fallback when no testrecordid in fake stub).

- [ ] **Step 5: Commit (manual, only if user asks)**

---

## Task 8: Fake Game-Flow Replay Tests

**Files:**
- Create: `Tests/CRPC.Tests/LordUnion/XmlReplayFakeGameFlowTests.cs`
- Reuse patterns from `ThreePlayersOneGameScenarioTests` fake scripts; extend game ack script to emit `LordInitCardAck` with `Testrecordid`.

- [ ] **Step 1: Write failing fake replay test**

```csharp
[Fact]
public void GameFlow_WithTestRecordId_ReplaysFirstBidFromFixture()
{
    // Build minimal fake script: Ready → InitCard(testrecordid + cards) → CallScore prompt
    // Run GameFlow.RunUntilFinishedAsync with coordinator-attached XmlReplayBotPolicy
    // Assert outbound LordCallScoreReq carries recorded CurScore from fixture
}
```

- [ ] **Step 2: Implement fake script helper** (`XmlReplayFakeScriptBuilder`) that drives one seat through init + first bid using fixture `20260601_7646425803181457480`.

- [ ] **Step 3: Run test — expect PASS**

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~XmlReplayFakeGameFlowTests"
```

- [ ] **Step 4: Add missing-fixture fail-fast test**

Assert `FileNotFoundException` propagates before bid when `testrecordid` points at non-existent fixture.

- [ ] **Step 5: Commit (manual, only if user asks)**

---

## Task 9: Live XmlReplay Scenario Test (Optional / Gated)

**Files:**
- Create: `Tests/CRPC.Tests/LordUnion/XmlReplayLiveTests.cs`

- [ ] **Step 1: Add live test with trait**

```csharp
[Fact]
[Trait("Category", "Live")]
public void ThreePlayers_XmlReplay_CompletesAgainstCasesServer()
{
    // Skip unless env var LORDUNION_LIVE=1 and server built with TK_INTEGRATION_TEST_USE_CASES
    var config = LoadLocalConfig();
    var report = ThreePlayersOneGameScenario.RunHosted(
        config,
        new ScenarioRunOptions { UseLiveTransport = true, SkipBotPacing = true });

    Assert.True(report.Success, report.FirstFailure?.Message);
}
```

- [ ] **Step 2: Document in spec verification section (already present); run manually**

```bash
set LORDUNION_LIVE=1
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "Category=Live&FullyQualifiedName~XmlReplay"
```

- [ ] **Step 3: Commit (manual, only if user asks)**

---

## Task 10: Server Track B (TKLordSvr)

Implement in `c:\work\TKLord\branch\dev-AIAdaptive\TKLordSvr` in parallel with client Tasks 1–6. Preserve GBK encoding on existing files; new files UTF-8 no BOM.

- [ ] **Step 1: Guard macros in `Define/TestDefine.h`**

```cpp
#ifdef TK_INTEGRATION_TEST_USE_CASES
#ifndef TK_INTEGRATION_TEST
#error TK_INTEGRATION_TEST_USE_CASES requires TK_INTEGRATION_TEST
#endif
#endif
```

- [ ] **Step 2: Add `RecordCaseReader` (new files under `Module/Test/`)**

- Scan `Record\TKLord\Cases\Origin\*.xml` in stable sorted order; track `m_nNextCaseIndex`.
- Parse deck bytes from first `id=27` action `o="[seat0][seat1][seat2]"` bracket groups.
- Expose `GetNextTestRecordId()` and `GetDeckForCase(const char* testRecordId)`.

- [ ] **Step 3: Inject deck + set `testrecordid` on `LordInitCardAck`**

In game init path guarded by `TK_INTEGRATION_TEST_USE_CASES`, call `RecordCaseReader`, inject cards, set string field on ack (match `Protocol/TKLord.proto` field 5).

- [ ] **Step 4: Redirect `LocalRecordGenerator::DumpToFile` paths**

| Condition | Output path |
|-----------|-------------|
| `TK_INTEGRATION_TEST_USE_CASES` | `Record\TKLord\Cases\Generate\{testrecordid}.xml` |
| `TK_INTEGRATION_TEST` only | `Record\TKLord\OnlyLordTakeOut\` (replace `Log\Record\`) |

- [ ] **Step 5: Add `RecordSemanticDiff`**

Load Origin + Generate for same stem; extract `<a id="2">` and `<a id="10">` sequences (`s`, `o` only); compare `<result seat score>` values; ignore `rt`, timestamps, match metadata.

On mismatch: log error with first differing action index; fail IT assert (follow existing IT logging pattern in `TKLordGame.cpp`).

- [ ] **Step 6: Hook diff after `DumpGameRecordToFile()` (~line 1039 in `Game/TKLordGame.cpp`)**

```cpp
#ifdef TK_INTEGRATION_TEST_USE_CASES
    VerifyGeneratedRecordMatchesOrigin(m_currentTestRecordId);
#endif
```

- [ ] **Step 7: Manual server verification**

Build IT+Cases configuration; run one table; confirm Generate written, diff passes, client receives consistent `testrecordid`.

- [ ] **Step 8: Commit via SVN (manual, only if user asks — SVN repo)**

---

## Final Verification Checklist

Run in order after all client tasks:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~XmlCardCodecTests|FullyQualifiedName~XmlRecordParserTests|FullyQualifiedName~XmlReplayBotPolicyTests|FullyQualifiedName~XmlReplayCoordinatorTests|FullyQualifiedName~XmlReplayFakeGameFlowTests"
```

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~ThreePlayersOneGameScenarioTests"
```

Expected: all PASS; live test skipped unless `LORDUNION_LIVE=1`.

---

## Spec Coverage Self-Review

| Spec requirement | Task |
|------------------|------|
| `LordInitCardAck.testrecordid` proto + plumbing | Task 1 |
| Fixture path `Games/TKLord/Cases/{id}.xml` | Task 3–4, csproj copy |
| Replay id=2 and id=10 per seat | Task 3, 6 |
| Fail-fast missing fixture | Task 4–5, 8 |
| Three-seat testrecordid agreement | Task 5 |
| No fallback to MinimalLandlord when replay active | Task 5–6 |
| Empty testrecordid → unchanged minimal bot | Task 5–7 |
| ImmediateActionScheduler for replay | Task 7 |
| Failure report fields | Task 7 |
| Server B semantic diff | Task 10 |
| Do not use LordSvr2BotRecordAck | No task (explicitly avoided) |

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-01-lordunion-xml-replay.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** — implement tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
