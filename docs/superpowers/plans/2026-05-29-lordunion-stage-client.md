# LordUnion Stage Client Implementation Plan

> **Historical plan (2026-05-29).** Current behavior and status: see `docs/superpowers/specs/2026-05-29-lordunion-stage-client-design.md`, `lordunion-game-stage-client-design.md`, `lordunion-enter-match-flow-internalize-design.md`, and `lordunion-cleanup-phases-0-3c-design.md`. Sections referencing `EnterMatchFlowResult`, `GameFlow.RunAsync`, or public `EnterMatchFlow` are outdated.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a stage-oriented LordUnion client so the three-account scenario reads as `Connect`, `Login`, `Signup`, `MatchStart`, `EnterMatch`, `EnterRound`, then `Game`.

**Architecture:** Add `LordUnionGameProfile` and typed stage result records under `Scenarios`, then add a per-account `LordUnionSessionClient` under `Sessions`. The stage client reuses `AccountSession`, `IGameServerTransport`, `ServerProtocolCodec`, and existing `EnterMatchFlow` behavior instead of changing the wire protocol or rewriting the game bot. `ThreePlayersOneGameScenario` migrates to stage calls after focused tests prove the stage client preserves current behavior.

**Tech Stack:** C# 12 / .NET 8, `CRpcTask`, `CRpcLoop`, xUnit, existing LordUnion fake transport and protocol helpers.

**Important:** Do not commit automatically. Commit checkpoints in this plan are manual review points only; create a git commit only if the user explicitly asks.

---

## File Structure

- Create `Tests/LordUnion.IntegrationTests/Scenarios/LordUnionGameProfile.cs`: profile and factory for common stage parameters plus selected `ILordGameVariant`.
- Create `Tests/LordUnion.IntegrationTests/Scenarios/StageResults.cs`: typed stage result records.
- Create `Tests/LordUnion.IntegrationTests/Sessions/LordUnionSessionClient.cs`: one-account stage facade.
- Create `Tests/CRPC.Tests/LordUnion/LordUnionSessionClientTests.cs`: focused unit tests for profile-driven calls and stage behavior.
- Modify `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`: use stage clients for common lifecycle stages after tests are green.
- Modify `Tests/LordUnion.IntegrationTests/Reporting/ReportWriter.cs`: make successful console output compact while preserving detailed failure diagnostics.
- Modify or add tests under `Tests/CRPC.Tests/LordUnion/`: scenario and report assertions as needed.

## Task 1: Add Game Profile And Stage Result Types

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Scenarios/LordUnionGameProfile.cs`
- Create: `Tests/LordUnion.IntegrationTests/Scenarios/StageResults.cs`
- Test: `Tests/CRPC.Tests/LordUnion/LordUnionGameProfileTests.cs`

- [ ] **Step 1: Write the failing profile test**

Create `Tests/CRPC.Tests/LordUnion/LordUnionGameProfileTests.cs`:

```csharp
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Scenarios;

namespace CRPC.Tests.LordUnion;

public sealed class LordUnionGameProfileTests
{
    [Fact]
    public void FromConfigCopiesMatchParametersAndVariant()
    {
        var match = new MatchConfig
        {
            GameId = 1001,
            ProductId = 2008280,
            TourneyId = 159740,
        };
        var variant = new ClassicLordVariant();

        var profile = LordUnionGameProfiles.FromConfig(match, variant);

        Assert.Equal("classic", profile.ProfileId);
        Assert.Equal(1001u, profile.GameId);
        Assert.Equal(2008280u, profile.ProductId);
        Assert.Equal(159740u, profile.TourneyId);
        Assert.Equal(2008280u, profile.MatchPoint);
        Assert.Same(variant, profile.Variant);
    }

    [Fact]
    public void FromConfigRejectsNullInputs()
    {
        var match = new MatchConfig();
        var variant = new ClassicLordVariant();

        Assert.Throws<ArgumentNullException>(() => LordUnionGameProfiles.FromConfig(null!, variant));
        Assert.Throws<ArgumentNullException>(() => LordUnionGameProfiles.FromConfig(match, null!));
    }
}
```

- [ ] **Step 2: Run the profile test to verify it fails**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnionGameProfileTests
```

Expected: fail because `LordUnionGameProfile` and `LordUnionGameProfiles` do not exist.

- [ ] **Step 3: Add `LordUnionGameProfile`**

Create `Tests/LordUnion.IntegrationTests/Scenarios/LordUnionGameProfile.cs`:

```csharp
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.GameVariants;

namespace LordUnion.IntegrationTests.Scenarios;

public sealed record LordUnionGameProfile
{
    public string ProfileId { get; init; } = "classic";

    public uint GameId { get; init; }

    public uint ProductId { get; init; }

    public uint TourneyId { get; init; }

    public uint MatchPoint { get; init; }

    public required ILordGameVariant Variant { get; init; }
}

public static class LordUnionGameProfiles
{
    public static LordUnionGameProfile FromConfig(MatchConfig match, ILordGameVariant variant)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(variant);

        return new LordUnionGameProfile
        {
            ProfileId = variant.VariantId,
            GameId = match.GameId,
            ProductId = match.ProductId,
            TourneyId = match.TourneyId,
            MatchPoint = match.ProductId,
            Variant = variant,
        };
    }
}
```

- [ ] **Step 4: Add stage result records**

Create `Tests/LordUnion.IntegrationTests/Scenarios/StageResults.cs`:

```csharp
namespace LordUnion.IntegrationTests.Scenarios;

public sealed record LoginStageResult(
    int Result,
    uint UserId,
    ulong? SessionId,
    string? Nickname,
    string AesKey);

public sealed record SignupStageResult(
    int Result,
    uint TourneyId,
    uint MatchPoint,
    uint GameId);

public sealed record MatchStartStageResult(
    uint MatchId,
    string? ServerIp,
    uint? ServerPort);

public sealed record EnterMatchStageResult(
    uint MatchId,
    uint? TableId);

public sealed record EnterRoundStageResult(
    uint MatchId,
    uint TableId,
    uint Seat);
```

- [ ] **Step 5: Run the profile test to verify it passes**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnionGameProfileTests
```

Expected: pass.

- [ ] **Step 6: Manual checkpoint**

Review the added files. Do not commit unless the user explicitly asks.

## Task 2: Add Stage Client Skeleton And Request/Response Helper

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Sessions/LordUnionSessionClient.cs`
- Test: `Tests/CRPC.Tests/LordUnion/LordUnionSessionClientTests.cs`

- [ ] **Step 1: Write the failing skeleton tests**

Create `Tests/CRPC.Tests/LordUnion/LordUnionSessionClientTests.cs`:

```csharp
using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

public sealed class LordUnionSessionClientTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();

    [Fact]
    public void ConstructorRejectsNullInputs()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();

        Assert.Throws<ArgumentNullException>(() => new LordUnionSessionClient(null!, transport, codec));
        Assert.Throws<ArgumentNullException>(() => new LordUnionSessionClient(session, null!, codec));
        Assert.Throws<ArgumentNullException>(() => new LordUnionSessionClient(session, transport, null!));
    }

    [Fact]
    public void ExposesSessionAndAlias()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();

        var client = new LordUnionSessionClient(session, transport, codec);

        Assert.Same(session, client.Session);
        Assert.Equal("player1", client.Alias);
    }

    [Fact]
    public void ConnectAsyncBindsTransportAndSetsConnectedState()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));

            Assert.Equal(AccountSessionState.Connected, session.State);
            transport.DeliverIncomingMessage(new ProtocolMessage
            {
                Header0 = 123,
                Kind = ProtocolMessageKind.Unknown,
            });

            await CRpcTask.Delay(1, loop);
            Assert.Single(session.ReceivedMessages);
        });
    }
}
```

- [ ] **Step 2: Run the skeleton tests to verify they fail**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnionSessionClientTests
```

Expected: fail because `LordUnionSessionClient` does not exist.

- [ ] **Step 3: Add the stage client skeleton**

Create `Tests/LordUnion.IntegrationTests/Sessions/LordUnionSessionClient.cs`:

```csharp
using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Sessions;

public sealed class LordUnionSessionClient
{
    private readonly AccountSession session;
    private readonly IGameServerTransport transport;
    private readonly ServerProtocolCodec codec;

    public LordUnionSessionClient(
        AccountSession session,
        IGameServerTransport transport,
        ServerProtocolCodec codec)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
    }

    public AccountSession Session => session;

    public string Alias => session.Alias;

    public async CRpcTask ConnectAsync(ServerConfig server, TimeSpan timeout)
    {
        EnsureOnLoopThread();
        ArgumentNullException.ThrowIfNull(server);

        session.SetState(AccountSessionState.Connecting);
        transport.BindIncomingHandler(session, codec);
        await transport.ConnectAsync(server, timeout, session.Loop);
        session.SetState(AccountSessionState.Connected);
    }

    private async CRpcTask<(int Result, TAck Ack, ProtocolMessage Message)> CallAsync<TAck>(
        TKMobileReqMsg request,
        ProtocolMessageKind expectedKind,
        Func<ProtocolMessage, TAck?> getAck,
        int timeoutMs)
        where TAck : class
    {
        await SendRequestAsync(request);

        var message = await session.WaitForMessageAsync(expectedKind, timeoutMs);
        var ack = getAck(message);
        if (ack is null)
        {
            throw new InvalidOperationException(
                $"[{session.Alias}] {expectedKind} missing typed acknowledgement. ack.param={message.Param}.");
        }

        return ((int)message.Param, ack, message);
    }

    private async CRpcTask SendRequestAsync(TKMobileReqMsg request)
    {
        await session.SendRequestAsync(request);
        if (session.LastSentPacket is not null)
        {
            await transport.SendAsync(session.LastSentPacket, session.Loop);
        }
    }

    private static int ToTimeoutMilliseconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
    }

    private void EnsureOnLoopThread()
    {
        if (!session.Loop.IsInLoopThread)
        {
            throw new InvalidOperationException(
                $"LordUnionSessionClient for account '{session.Alias}' must run on the owner CRpcLoop thread.");
        }
    }
}
```

- [ ] **Step 4: Run the skeleton tests to verify they pass**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnionSessionClientTests
```

Expected: pass.

- [ ] **Step 5: Manual checkpoint**

Review constructor, loop ownership, and transport binding behavior. Do not commit unless the user explicitly asks.

## Task 3: Implement `LoginAsync`

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Sessions/LordUnionSessionClient.cs`
- Test: `Tests/CRPC.Tests/LordUnion/LordUnionSessionClientTests.cs`

- [ ] **Step 1: Add a failing login test**

Add these `using` directives to `LordUnionSessionClientTests`:

```csharp
using LordUnion.IntegrationTests.Protocol.Generated;
```

Append this test and helper to `LordUnionSessionClientTests`:

```csharp
[Fact]
public void LoginAsync_CompletesBrowseAndLogin()
{
    var loop = new CRpcLoop();
    var session = new AccountSession(loop, "player1", codec);
    var transport = new FakeGameServerTransport();
    var client = new LordUnionSessionClient(session, transport, codec);

    transport.OnPacketSentAsync = (packet, packetLoop) =>
    {
        var sent = transport.DecodeSentPacket(
            packet,
            new ProtocolDecodeContext
            {
                AccountAlias = session.Alias,
                Phase = session.CurrentPhase,
            });

        if (sent.Kind == ProtocolMessageKind.AnonymousBrowseReq)
        {
            transport.DeliverIncomingMessage(CreateAnonymousBrowseAck(header0: 3001, aesKey: string.Empty));
        }
        else if (sent.Kind == ProtocolMessageKind.CommonLoginReq)
        {
            transport.DeliverIncomingMessage(CreateCommonLoginAck(header0: 3002, userId: 214291552, nickname: "player-one"));
        }

        return CRpcTask.CompletedTask(packetLoop);
    };

    CRpcLoopRunner.RunUntilComplete(loop, async () =>
    {
        await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));

        var result = await client.LoginAsync(
            new AccountConfig
            {
                Alias = "player1",
                Username = "user",
                Password = "pass",
            },
            new ProtocolConfig
            {
                AppId = 2,
                AnonymousSerialId = 1,
                LoginType = 2,
            },
            TimeSpan.FromSeconds(5));

        Assert.Equal(0, result.Result);
        Assert.Equal(214291552u, result.UserId);
        Assert.Equal("player-one", result.Nickname);
        Assert.Equal(LobbyAes128Crypto.DefaultKey, result.AesKey);
        Assert.Equal(AccountSessionState.LoggedIn, session.State);
        Assert.Equal(214291552u, session.UserId);
        Assert.Equal("player-one", session.Nickname);
        Assert.Equal(3001u, session.AnonymousRouteId);
        Assert.Equal(3002u, session.LoginRouteId);
        Assert.Equal(2, session.SentMessages.Count);
    });
}

private static ProtocolMessage CreateAnonymousBrowseAck(uint header0, string aesKey)
{
    return new ProtocolMessage
    {
        Header0 = header0,
        Kind = ProtocolMessageKind.AnonymousBrowseAck,
        Param = 0,
        AnonymousBrowseAcknowledgement = new AnonymousBrowseAck
        {
            Param = aesKey,
            Timesec = 1,
            Timeusec = 0,
        },
    };
}

private static ProtocolMessage CreateCommonLoginAck(uint header0, uint userId, string nickname)
{
    return new ProtocolMessage
    {
        Header0 = header0,
        Kind = ProtocolMessageKind.CommonLoginAck,
        Param = 0,
        CommonLoginAcknowledgement = new CommonLoginAck
        {
            Userinfo = new LcUserInfoEx
            {
                Userid = userId,
                Nickname = nickname,
            },
        },
    };
}
```

- [ ] **Step 2: Run the login test to verify it fails**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LoginAsync_CompletesBrowseAndLogin
```

Expected: fail because `LordUnionSessionClient.LoginAsync` does not exist.

- [ ] **Step 3: Implement `LoginAsync`**

Add these `using` directives to `LordUnionSessionClient.cs`:

```csharp
using System.Diagnostics;
using LordUnion.IntegrationTests.Scenarios;
```

Add this public method inside `LordUnionSessionClient`:

```csharp
public async CRpcTask<LoginStageResult> LoginAsync(
    AccountConfig account,
    ProtocolConfig protocol,
    TimeSpan timeout)
{
    EnsureOnLoopThread();
    ArgumentNullException.ThrowIfNull(account);
    ArgumentNullException.ThrowIfNull(protocol);

    var timeoutMs = ToTimeoutMilliseconds(timeout);

    try
    {
        var browseStopwatch = Stopwatch.StartNew();
        var (_, browseAck, browseMessage) = await CallAsync(
            codec.CreateAnonymousBrowseRequest(protocol.AnonymousSerialId),
            ProtocolMessageKind.AnonymousBrowseAck,
            static message => message.AnonymousBrowseAcknowledgement,
            timeoutMs);

        session.AnonymousRouteId = browseMessage.Header0;
        var aesKey = string.IsNullOrEmpty(browseAck.Param)
            ? LobbyAes128Crypto.DefaultKey
            : browseAck.Param!;
        session.AesKey = aesKey;

        var loginTimestampMillis = browseAck.ResolveLoginTimestampMillis(browseStopwatch.ElapsedMilliseconds);
        var (resultCode, commonLoginAck, loginMessage) = await CallAsync(
            codec.CreatePasswordLoginRequest(
                account.Username,
                account.Password,
                aesKey,
                protocol.AppId,
                loginTimestampMillis,
                protocol.LoginType),
            ProtocolMessageKind.CommonLoginAck,
            static message => message.CommonLoginAcknowledgement,
            timeoutMs);

        session.LoginRouteId = loginMessage.Header0;
        var decryptedLoginJson = DecryptLoginAckJson(commonLoginAck, aesKey);
        var userId = commonLoginAck.Userinfo?.Userid
            ?? LoginAckJsonParser.TryGetUserId(decryptedLoginJson);
        var sessionId = LoginAckJsonParser.TryGetSessionId(decryptedLoginJson);
        var nickname = commonLoginAck.Userinfo?.Nickname;
        var success = userId is > 0
            && (loginMessage.Param == 0 || (loginMessage.Param == 31 && sessionId is > 0));

        session.UserId = userId;
        session.Nickname = nickname;
        session.SessionId = sessionId;
        session.LoginErrorCode = loginMessage.Param;

        if (!success)
        {
            session.SetState(AccountSessionState.Failed);
            throw new InvalidOperationException(
                $"[{session.Alias}] Login failed: {BuildLoginFailureMessage(loginMessage.Param, userId, decryptedLoginJson)}");
        }

        session.SetState(AccountSessionState.LoggedIn);
        return new LoginStageResult(
            resultCode,
            userId.GetValueOrDefault(),
            sessionId,
            nickname,
            aesKey);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        if (session.State != AccountSessionState.Failed)
        {
            session.SetState(AccountSessionState.Failed);
        }

        throw;
    }
}
```

Add these private methods inside `LordUnionSessionClient`:

```csharp
private static string? DecryptLoginAckJson(CommonLoginAck commonLoginAck, string aesKey)
{
    if (commonLoginAck.Cryptotype == 1 && !string.IsNullOrEmpty(commonLoginAck.Jsondata))
    {
        return LobbyAes128Crypto.DecryptFromHex(commonLoginAck.Jsondata!, aesKey);
    }

    return commonLoginAck.Jsondata;
}

private static string BuildLoginFailureMessage(uint loginErrorCode, uint? userId, string? decryptedLoginJson)
{
    if (loginErrorCode != 0 && loginErrorCode != 31)
    {
        return $"CommonLoginAck param={loginErrorCode}. ackJson={decryptedLoginJson}";
    }

    if (loginErrorCode == 31)
    {
        return $"CommonLoginAck param=31 without a valid session id. ackJson={decryptedLoginJson}";
    }

    if (userId is null or 0)
    {
        return $"CommonLoginAck had no userid. ackJson={decryptedLoginJson}";
    }

    return "CommonLoginAck did not satisfy success conditions.";
}
```

- [ ] **Step 4: Run the login test to verify it passes**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LoginAsync_CompletesBrowseAndLogin
```

Expected: pass.

- [ ] **Step 5: Run all stage client tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnionSessionClientTests
```

Expected: pass.

- [ ] **Step 6: Manual checkpoint**

Compare the login logic with `LoginFlow`. Confirm route id, AES key, user id, session id, nickname, and failure behavior match. Do not commit unless the user explicitly asks.

## Task 4: Implement `SignupAsync`

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Sessions/LordUnionSessionClient.cs`
- Test: `Tests/CRPC.Tests/LordUnion/LordUnionSessionClientTests.cs`

- [ ] **Step 1: Add a failing signup test**

Add these `using` directives to `LordUnionSessionClientTests`:

```csharp
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Scenarios;
```

Append this test and helper to `LordUnionSessionClientTests`:

```csharp
[Fact]
public void SignupAsync_SendsProfileParameters()
{
    var loop = new CRpcLoop();
    var session = new AccountSession(loop, "player1", codec)
    {
        UserId = 214291552,
        Nickname = "player-one",
    };
    var transport = new FakeGameServerTransport();
    var client = new LordUnionSessionClient(session, transport, codec);
    ProtocolMessage? signupRequest = null;

    transport.OnPacketSentAsync = (packet, packetLoop) =>
    {
        var sent = transport.DecodeSentPacket(
            packet,
            new ProtocolDecodeContext
            {
                AccountAlias = session.Alias,
                Phase = session.CurrentPhase,
            });
        if (sent.Kind == ProtocolMessageKind.TourneySignupReq)
        {
            signupRequest = sent;
            transport.DeliverIncomingMessage(CreateTourneySignupAck(
                tourneyId: 159740,
                matchPoint: 2008280,
                gameId: 1001));
        }

        return CRpcTask.CompletedTask(packetLoop);
    };

    CRpcLoopRunner.RunUntilComplete(loop, async () =>
    {
        session.SetState(AccountSessionState.LoggedIn);
        await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));
        session.SetState(AccountSessionState.LoggedIn);

        var result = await client.SignupAsync(
            new LordUnionGameProfile
            {
                ProfileId = "classic",
                GameId = 1001,
                ProductId = 2008280,
                TourneyId = 159740,
                MatchPoint = 2008280,
                Variant = new ClassicLordVariant(),
            },
            TimeSpan.FromSeconds(5));

        Assert.NotNull(signupRequest);
        Assert.Equal(ProtocolMessageKind.TourneySignupReq, signupRequest!.Kind);
        Assert.Equal(0, result.Result);
        Assert.Equal(159740u, result.TourneyId);
        Assert.Equal(2008280u, result.MatchPoint);
        Assert.Equal(1001u, result.GameId);
        Assert.Equal(159740u, session.TourneyId);
        Assert.Equal(2008280u, session.MatchPoint);
        Assert.Equal(AccountSessionState.SignedUp, session.State);
    });
}

private static ProtocolMessage CreateTourneySignupAck(uint tourneyId, uint matchPoint, uint gameId)
{
    return new ProtocolMessage
    {
        Header0 = 4001,
        Kind = ProtocolMessageKind.TourneySignupAck,
        Param = 0,
        TourneySignupAcknowledgement = new TourneySignupExAck
        {
            Param = 0,
            Tourneyid = tourneyId,
            Matchpoint = matchPoint,
            Gameid = gameId,
        },
    };
}
```

- [ ] **Step 2: Run the signup test to verify it fails**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter SignupAsync_SendsProfileParameters
```

Expected: fail because `LordUnionSessionClient.SignupAsync` does not exist.

- [ ] **Step 3: Implement `SignupAsync`**

Add this `using` directive to `LordUnionSessionClient.cs` if it is not already present:

```csharp
using LordUnion.IntegrationTests.Scenarios;
```

Add this public method inside `LordUnionSessionClient`:

```csharp
public async CRpcTask<SignupStageResult> SignupAsync(
    LordUnionGameProfile profile,
    TimeSpan timeout)
{
    EnsureOnLoopThread();
    ArgumentNullException.ThrowIfNull(profile);

    if (session.State != AccountSessionState.LoggedIn)
    {
        throw new InvalidOperationException(
            $"[{session.Alias}] Signup requires LoggedIn state; current state is {session.State}.");
    }

    if (session.UserId is not uint userId || userId == 0)
    {
        throw new InvalidOperationException(
            $"[{session.Alias}] Signup requires a non-zero UserId.");
    }

    try
    {
        var (resultCode, signupAck, _) = await CallAsync(
            codec.CreateTourneySignupRequest(
                userId,
                profile.TourneyId,
                profile.GameId,
                matchPoint: profile.MatchPoint,
                nickname: session.Nickname),
            ProtocolMessageKind.TourneySignupAck,
            static message => message.TourneySignupAcknowledgement,
            ToTimeoutMilliseconds(timeout));

        if (signupAck.Param != 0)
        {
            session.SetState(AccountSessionState.Failed);
            throw new InvalidOperationException(
                $"[{session.Alias}] Signup failed: TourneySignupAck param={signupAck.Param}.");
        }

        session.TourneyId = signupAck.Tourneyid;
        session.MatchPoint = signupAck.Matchpoint;
        session.SetState(AccountSessionState.SignedUp);

        return new SignupStageResult(
            resultCode,
            signupAck.Tourneyid,
            signupAck.Matchpoint,
            signupAck.Gameid);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        if (session.State != AccountSessionState.Failed)
        {
            session.SetState(AccountSessionState.Failed);
        }

        throw;
    }
}
```

- [ ] **Step 4: Run the signup test to verify it passes**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter SignupAsync_SendsProfileParameters
```

Expected: pass.

- [ ] **Step 5: Run all stage client tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnionSessionClientTests
```

Expected: pass.

- [ ] **Step 6: Manual checkpoint**

Confirm `SignupAsync` uses `profile.MatchPoint` rather than directly reading `MatchConfig.ProductId`. Do not commit unless the user explicitly asks.

## Task 5: Add MatchStart, EnterMatch, And EnterRound Stage Methods Using Existing Flow

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Sessions/LordUnionSessionClient.cs`
- Test: `Tests/CRPC.Tests/LordUnion/LordUnionSessionClientTests.cs`

- [ ] **Step 1: Add failing wrappers tests**

Confirm these `using` directives exist in `LordUnionSessionClientTests`:

```csharp
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Scenarios;
```

Append these tests to `LordUnionSessionClientTests`:

```csharp
[Fact]
public void WaitForMatchStartAsync_CompletesFromStartClientExAck()
{
    var loop = new CRpcLoop();
    var session = new AccountSession(loop, "player1", codec);
    var transport = new FakeGameServerTransport();
    var client = new LordUnionSessionClient(session, transport, codec);

    CRpcLoopRunner.RunUntilComplete(loop, async () =>
    {
        session.SetState(AccountSessionState.SignedUp);
        var waitTask = client.WaitForMatchStartAsync(TimeSpan.FromSeconds(5));

        transport.DeliverIncomingMessage(new ProtocolMessage
        {
            Header0 = 5001,
            Kind = ProtocolMessageKind.StartClientExAck,
            Param = 0,
            StartClientExAcknowledgement = new StartClientExAck
            {
                Matchid = 475051269,
                Gameid = 1001,
                Tourneyid = 159740,
                Matchpoint = 2008280,
                Ip = "127.0.0.1",
                Port = 30301,
            },
        });

        var result = await waitTask;

        Assert.Equal(475051269u, result.MatchId);
        Assert.Equal("127.0.0.1", result.ServerIp);
        Assert.Equal(30301u, result.ServerPort);
        Assert.Equal(475051269u, session.MatchId);
    });
}

[Fact]
public void EnterMatchAsync_SendsEnterMatchRequest()
{
    var loop = new CRpcLoop();
    var session = new AccountSession(loop, "player1", codec)
    {
        UserId = 214291552,
    };
    var transport = new FakeGameServerTransport();
    var client = new LordUnionSessionClient(session, transport, codec);
    var profile = new LordUnionGameProfile
    {
        ProfileId = "classic",
        GameId = 1001,
        ProductId = 2008280,
        TourneyId = 159740,
        MatchPoint = 2008280,
        Variant = new ClassicLordVariant(),
    };

    transport.OnPacketSentAsync = (packet, packetLoop) =>
    {
        var sent = transport.DecodeSentPacket(
            packet,
            new ProtocolDecodeContext
            {
                AccountAlias = session.Alias,
                Phase = session.CurrentPhase,
            });

        if (sent.Kind == ProtocolMessageKind.EnterMatchReq)
        {
            transport.DeliverIncomingMessage(new ProtocolMessage
            {
                Header0 = 5002,
                Kind = ProtocolMessageKind.EnterMatchAck,
                Param = 0,
                EnterMatchAcknowledgement = new EnterMatchAck
                {
                    Matchid = 475051269,
                },
            });
        }

        return CRpcTask.CompletedTask(packetLoop);
    };

    CRpcLoopRunner.RunUntilComplete(loop, async () =>
    {
        session.SetState(AccountSessionState.SignedUp);

        var result = await client.EnterMatchAsync(
            profile,
            new MatchStartStageResult(475051269, "127.0.0.1", 30301),
            TimeSpan.FromSeconds(5));

        Assert.Equal(475051269u, result.MatchId);
        Assert.Equal(475051269u, result.TableId);
        Assert.Equal(AccountSessionState.EnteringMatch, session.State);
    });
}
```

- [ ] **Step 2: Run wrapper tests to verify they fail**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "WaitForMatchStartAsync_CompletesFromStartClientExAck|EnterMatchAsync_SendsEnterMatchRequest"
```

Expected: fail because stage wrapper methods do not exist.

- [ ] **Step 3: Add `EnterMatchFlow` dependency to the client**

Add this `using` directive to `LordUnionSessionClient.cs`:

```csharp
using LordUnion.IntegrationTests.Flows;
```

Add a field:

```csharp
private readonly EnterMatchFlow enterMatchFlow;
private readonly EnterMatchFlowSessionState enterMatchState = new();
private EnterMatchStartInfo? lastMatchStartInfo;
```

Change the constructor signature:

```csharp
public LordUnionSessionClient(
    AccountSession session,
    IGameServerTransport transport,
    ServerProtocolCodec codec,
    EnterMatchFlow? enterMatchFlow = null)
{
    this.session = session ?? throw new ArgumentNullException(nameof(session));
    this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
    this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
    this.enterMatchFlow = enterMatchFlow ?? new EnterMatchFlow(codec);
}
```

- [ ] **Step 4: Add `WaitForMatchStartAsync`**

Add this method inside `LordUnionSessionClient`:

```csharp
public async CRpcTask<MatchStartStageResult> WaitForMatchStartAsync(TimeSpan timeout)
{
    EnsureOnLoopThread();

    var matchStart = await enterMatchFlow.WaitForMatchStartAsync(
        session,
        timeout,
        transport,
        enterMatchState);
    enterMatchFlow.ApplyMatchStartToSession(session, matchStart);
    lastMatchStartInfo = matchStart;
    var capturedStartClient = enterMatchState.CapturedMatchStartMessage?.StartClientExAcknowledgement;

    return new MatchStartStageResult(
        matchStart.MatchId,
        capturedStartClient?.Ip,
        capturedStartClient?.Port);
}
```

- [ ] **Step 5: Add `EnterMatchAsync` and `EnterRoundAsync`**

Add these methods inside `LordUnionSessionClient`:

```csharp
public async CRpcTask<EnterMatchStageResult> EnterMatchAsync(
    LordUnionGameProfile profile,
    MatchStartStageResult matchStart,
    TimeSpan timeout)
{
    EnsureOnLoopThread();
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(matchStart);

    var startInfo = CreateStartInfo(profile, matchStart);
    await enterMatchFlow.EnterMatchOnlyAsync(
        session,
        startInfo,
        timeout,
        transport);

    return new EnterMatchStageResult(
        startInfo.MatchId,
        session.TableId);
}

public async CRpcTask<EnterRoundStageResult> EnterRoundAsync(
    LordUnionGameProfile profile,
    TimeSpan timeout)
{
    EnsureOnLoopThread();
    ArgumentNullException.ThrowIfNull(profile);

    if (session.MatchId is not uint matchId || matchId == 0)
    {
        throw new InvalidOperationException($"[{session.Alias}] EnterRound requires a non-zero MatchId.");
    }

    var startInfo = new EnterMatchStartInfo
    {
        MatchId = matchId,
        GameId = profile.GameId,
        TourneyId = profile.TourneyId,
        MatchPoint = profile.MatchPoint,
        Ticket = session.Ticket ?? Array.Empty<byte>(),
    };

    var seat = await enterMatchFlow.EnterRoundOnlyAsync(
        session,
        startInfo,
        timeout,
        transport,
        enterMatchState);

    if (session.TableId is not uint tableId || tableId == 0)
    {
        tableId = matchId;
        session.TableId = tableId;
    }

    return new EnterRoundStageResult(matchId, tableId, seat);
}

private EnterMatchStartInfo CreateStartInfo(
    LordUnionGameProfile profile,
    MatchStartStageResult matchStart)
{
    if (lastMatchStartInfo is { } cached && cached.MatchId == matchStart.MatchId)
    {
        return cached;
    }

    return new EnterMatchStartInfo
    {
        MatchId = matchStart.MatchId,
        GameId = profile.GameId,
        TourneyId = profile.TourneyId,
        MatchPoint = profile.MatchPoint,
        Ticket = session.Ticket ?? Array.Empty<byte>(),
    };
}
```

- [ ] **Step 6: Run wrapper tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "WaitForMatchStartAsync_CompletesFromStartClientExAck|EnterMatchAsync_SendsEnterMatchRequest"
```

Expected: pass after any generated-type/property-name corrections.

- [ ] **Step 7: Run all stage client tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnionSessionClientTests
```

Expected: pass.

- [ ] **Step 8: Manual checkpoint**

Review whether `EnterMatchFlow` behavior stayed intact. Do not commit unless the user explicitly asks.

## Task 6: Migrate Scenario Bundle Creation To Include Stage Clients

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`
- Test: existing scenario tests under `Tests/CRPC.Tests/LordUnion`

- [ ] **Step 1: Find existing scenario tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --list-tests | rg "ThreePlayers|Scenario|LordUnion"
```

Expected: list the current LordUnion scenario tests. Use those test names for the later filtered runs.

- [ ] **Step 2: Add `LordUnionSessionClient` to `AccountBundle`**

In `ThreePlayersOneGameScenario.cs`, find the private account bundle type near the bottom of the file. Add a client property:

```csharp
private sealed class AccountBundle
{
    public required AccountConfig Account { get; init; }

    public required AccountSession Session { get; init; }

    public required IGameServerTransport Transport { get; init; }

    public required LordUnionSessionClient Client { get; init; }

    public required AccountPhaseTiming Timing { get; init; }
}
```

Preserve any existing properties on `AccountBundle` that are not shown here.

- [ ] **Step 3: Update `CreateBundle`**

In `CreateBundle`, construct the client after the session and transport are created:

```csharp
var session = new AccountSession(loop, account.Alias, codec);
var transport = factory.CreateTransport(session, account);
var client = new LordUnionSessionClient(session, transport, codec, enterMatchFlow);

return new AccountBundle
{
    Account = account,
    Session = session,
    Transport = transport,
    Client = client,
    Timing = new AccountPhaseTiming { AccountAlias = account.Alias },
};
```

If the existing `AccountBundle` contains additional fields, keep them unchanged and add `Client = client`.

- [ ] **Step 4: Run scenario tests**

Run the filtered scenario tests discovered in Step 1. If there is no narrow filter, run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnion
```

Expected: pass. No behavior should have changed yet because stage methods are not wired into the scenario.

- [ ] **Step 5: Manual checkpoint**

Review bundle construction only. Do not commit unless the user explicitly asks.

## Task 7: Migrate Login And Signup Phases To Stage Client

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`
- Test: existing LordUnion scenario tests

- [ ] **Step 1: Add profile creation near bundle setup**

In `RunCoreAsync`, after bundles are created and before phases run, add:

```csharp
var profile = LordUnionGameProfiles.FromConfig(config.Match, variant);
```

- [ ] **Step 2: Replace `RunLoginAsync` body with stage call**

Find `RunLoginAsync`. Keep its signature so `RunPhaseConcurrentOnLoopAsync` does not need to change. Replace the inner flow call with:

```csharp
var login = await bundle.Client.LoginAsync(
    bundle.Account,
    config.Protocol,
    config.Timeouts.LoginTimeout);

return new LoginFlowResult
{
    Success = true,
    UserId = login.UserId,
    Nickname = login.Nickname,
    AesKey = login.AesKey,
    SessionId = login.SessionId,
    LoginErrorCode = (uint)login.Result,
    AnonymousRouteId = bundle.Session.AnonymousRouteId ?? 0,
    LoginRouteId = bundle.Session.LoginRouteId ?? 0,
    DecryptedLoginAckJson = null,
    FailureMessage = null,
};
```

- [ ] **Step 3: Ensure connect happens before login**

If the old `LoginFlow.RunAsync` was the only place that connected the transport, call connect at the start of `RunLoginAsync`:

```csharp
await bundle.Client.ConnectAsync(config.Server, config.Timeouts.ConnectTimeout);
```

The login phase timing can include connect time for now, matching current behavior in `LoginFlow`.

- [ ] **Step 4: Replace `RunSignupAsync` body with stage call**

Change the `RunSignupAsync` signature to accept the profile:

```csharp
private async CRpcTask<SignupFlowResult> RunSignupAsync(
    AccountBundle bundle,
    LordUnionTestConfig config,
    LordUnionGameProfile profile,
    EnterMatchFlowSessionState matchProgressState,
    CancellationToken cancellationToken)
```

Update the caller to pass `profile`:

```csharp
bundle => RunSignupAsync(bundle, config, profile, matchProgressStates[bundle.Session.Alias], cancellationToken)
```

Then change the body to call:

```csharp
var signup = await bundle.Client.SignupAsync(
    profile,
    config.Timeouts.SignupTimeout);

matchProgressState.CaptureFromAnyMessage(new ProtocolMessage
{
    Header0 = bundle.Session.LoginRouteId ?? 0,
    Kind = ProtocolMessageKind.TourneySignupAck,
    Param = (uint)signup.Result,
    TourneySignupAcknowledgement = new TourneySignupExAck
    {
        Param = (uint)signup.Result,
        Tourneyid = signup.TourneyId,
        Matchpoint = signup.MatchPoint,
        Gameid = signup.GameId,
    },
});

return new SignupFlowResult
{
    Success = true,
    SignupErrorCode = (uint)signup.Result,
    MobileAckParam = (uint)signup.Result,
    Flags = 0,
    TourneyId = signup.TourneyId,
    MatchPoint = signup.MatchPoint,
    GameId = signup.GameId,
    FailureMessage = null,
};
```

- [ ] **Step 5: Run LordUnion tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnion
```

Expected: pass.

- [ ] **Step 6: Manual checkpoint**

Review whether login/signup failure messages still include account aliases and whether timings still populate. Do not commit unless the user explicitly asks.

## Task 8: Migrate MatchStart, EnterMatch, And EnterRound In Scenario

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`
- Test: existing LordUnion scenario tests

- [ ] **Step 1: Replace enter-match phase call with stage methods**

Find `RunEnterMatchAsync`. Keep its return type for now and add a profile parameter:

```csharp
private async CRpcTask<EnterMatchFlowResult> RunEnterMatchAsync(
    AccountBundle bundle,
    LordUnionTestConfig config,
    LordUnionGameProfile profile,
    ScenarioRunOptions options,
    CancellationToken cancellationToken)
```

Update both `RunEnterMatchAsync` callers to pass `profile`:

```csharp
bundle => RunEnterMatchAsync(bundle, config, profile, options, cancellationToken)
```

Then replace the body with:

```csharp
var matchStart = await bundle.Client.WaitForMatchStartAsync(config.Timeouts.MatchStartTimeout);
var enterMatch = await bundle.Client.EnterMatchAsync(
    profile,
    matchStart,
    config.Timeouts.EnterMatchTimeout);
var enterRound = await bundle.Client.EnterRoundAsync(
    profile,
    config.Timeouts.EnterRoundTimeout);

return new EnterMatchFlowResult
{
    Success = true,
    MatchId = enterRound.MatchId,
    TableId = enterRound.TableId,
    SeatOrder = enterRound.Seat,
    FailureMessage = null,
};
```

- [ ] **Step 2: Remove obsolete monitor wiring only after tests pass**

Do not delete `PostSignupDiagnosticMonitor` setup in the first edit. First run tests with the new stage methods. If monitor wiring conflicts because `WaitForMatchStartAsync` now installs capture itself, remove only the duplicate monitor installation in `RunEnterMatchPhaseAsync`, preserving failure diagnostics elsewhere.

- [ ] **Step 3: Run LordUnion tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnion
```

Expected: pass.

- [ ] **Step 4: Inspect scenario readability**

Open `ThreePlayersOneGameScenario.cs` and confirm the visible common lifecycle is now stage-oriented:

```text
ConnectAsync
LoginAsync
SignupAsync
WaitForMatchStartAsync
EnterMatchAsync
EnterRoundAsync
GameFlow
```

- [ ] **Step 5: Manual checkpoint**

Review the match-start and enter-round migration carefully. Do not commit unless the user explicitly asks.

## Task 9: Compact Successful Console Output

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Reporting/ReportWriter.cs`
- Test: add or update reporting tests under `Tests/CRPC.Tests/LordUnion`

- [ ] **Step 1: Add a failing report writer test**

Create or append to a report writer test file:

```csharp
using System.IO;
using LordUnion.IntegrationTests.Reporting;

namespace CRPC.Tests.LordUnion;

public sealed class ReportWriterTests
{
    [Fact]
    public void WriteConsoleSummary_UsesCompactSuccessOutput()
    {
        var report = new ScenarioReport
        {
            Success = true,
            MatchId = 475051269,
            TableId = 475051269,
            WinSeat = 1,
            AccountTimings =
            [
                new AccountPhaseTiming
                {
                    AccountAlias = "player1",
                    LoginDuration = TimeSpan.FromMilliseconds(416),
                    SignupDuration = TimeSpan.FromMilliseconds(74),
                    EnterMatchDuration = TimeSpan.FromMilliseconds(3440),
                    GameDuration = TimeSpan.FromMilliseconds(146300),
                    TotalDuration = TimeSpan.FromMilliseconds(150230),
                },
            ],
        };
        var metadata = new ReportMetadata
        {
            ScenarioName = "ThreePlayersOneGame",
            StartedAt = DateTimeOffset.Parse("2026-05-29T03:20:00Z"),
            EndedAt = DateTimeOffset.Parse("2026-05-29T03:22:35Z"),
        };
        using var writer = new StringWriter();
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(writer);
            new ReportWriter().WriteConsoleSummary(report, metadata);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("SUCCESS ThreePlayersOneGame", output, StringComparison.Ordinal);
        Assert.Contains("player1 login=416ms signup=74ms enter=3.44s game=146.30s", output, StringComparison.Ordinal);
        Assert.DoesNotContain("--- Signup Diagnostics ---", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Post-signup messages", output, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the reporting test to verify it fails**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter WriteConsoleSummary_UsesCompactSuccessOutput
```

Expected: fail because current success output is verbose.

- [ ] **Step 3: Implement compact success output**

In `ReportWriter.WriteConsoleSummary`, branch early:

```csharp
if (report.Success)
{
    WriteCompactSuccessSummary(report, metadata);
    return;
}
```

Add this helper inside `ReportWriter`:

```csharp
private static void WriteCompactSuccessSummary(ScenarioReport report, ReportMetadata metadata)
{
    Console.WriteLine(
        $"SUCCESS {metadata.ScenarioName} duration={FormatDuration(metadata.EndedAt - metadata.StartedAt)} " +
        $"matchId={report.MatchId?.ToString() ?? "(unknown)"} " +
        $"tableId={report.TableId?.ToString() ?? "(unknown)"} " +
        $"winSeat={report.WinSeat?.ToString() ?? "(unknown)"}");

    foreach (var timing in report.AccountTimings)
    {
        Console.WriteLine(
            $"{timing.AccountAlias} " +
            $"login={FormatDuration(timing.LoginDuration)} " +
            $"signup={FormatDuration(timing.SignupDuration)} " +
            $"enter={FormatDuration(timing.EnterMatchDuration)} " +
            $"game={FormatDuration(timing.GameDuration)}");
    }
}
```

Keep the existing verbose sections for failure output.

- [ ] **Step 4: Run the reporting test**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter WriteConsoleSummary_UsesCompactSuccessOutput
```

Expected: pass.

- [ ] **Step 5: Run LordUnion tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter LordUnion
```

Expected: pass.

- [ ] **Step 6: Manual checkpoint**

Confirm failure reports still include the detailed sections. Do not commit unless the user explicitly asks.

## Task 10: Regression And Live Verification

**Files:**
- No planned source changes.
- Read generated output under `Tests/LordUnion.IntegrationTests/lordunion-test-output`.

- [ ] **Step 1: Run CRPC regression tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected: all tests pass. Existing package compatibility warnings are acceptable if they match previous runs.

- [ ] **Step 2: Run LordUnion integration test project tests**

Run:

```bash
dotnet test Tests/LordUnion.IntegrationTests/LordUnion.IntegrationTests.csproj
```

Expected: all non-live tests pass.

- [ ] **Step 3: Run one live three-account scenario**

Run:

```bash
dotnet run --project Tests/LordUnion.IntegrationTests -- --live --config Tests/LordUnion.IntegrationTests/appsettings.local.json
```

Expected:

```text
SUCCESS ThreePlayersOneGame
JSON report: Tests/LordUnion.IntegrationTests/lordunion-test-output/scenario-report-<timestamp>.json
```

- [ ] **Step 4: Inspect the JSON report**

Open the new JSON report and confirm:

```json
{
  "success": true
}
```

Also confirm account timings and game result are present.

- [ ] **Step 5: Manual final review**

Review the final diff. Confirm:

- `ThreePlayersOneGameScenario` uses stage-level calls for common lifecycle stages.
- Scenario code no longer directly sends requests or extracts typed acks for login/signup/match-start/enter stages.
- `GameFlow` and `ILordGameVariant` still own game-specific behavior.
- Success logs are compact.
- Failure diagnostics remain available.

Do not commit unless the user explicitly asks.

## Self-Review Checklist

- Spec coverage: Tasks cover profile, stage results, per-account client, login, signup, match start, enter match, enter round, scenario migration, compact success logs, tests, and live verification.
- Scope check: The plan does not add a second game, does not generate protobuf stubs, and does not rewrite `GameFlow` or bot behavior.
- Type consistency: Method signatures match the design spec. The plan flags generated protobuf names and result property names that must be checked against existing files during implementation.
- User rule: The plan explicitly avoids automatic commits.
