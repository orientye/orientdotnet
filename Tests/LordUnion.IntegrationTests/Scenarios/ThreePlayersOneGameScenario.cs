using System.Diagnostics;
using System.Text.RegularExpressions;
using CRpc.Async;
using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.Bots.Pacing;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Reporting;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Sessions;


namespace LordUnion.IntegrationTests.Scenarios;

/// <summary>
/// Orchestrates three accounts through login, signup, enter-match, and one game.
/// All sessions share one <see cref="CRpcLoop"/> business thread.
/// </summary>
public sealed class ThreePlayersOneGameScenario

{
    private readonly ServerProtocolCodec codec;

    private readonly EnterMatchFlow? enterMatchFlow;

    private readonly GameFlow gameFlow;

    private readonly ILordGameVariant variant;


    public ThreePlayersOneGameScenario(
        ServerProtocolCodec? codec = null,
        ILordGameVariant? variant = null,
        EnterMatchFlow? enterMatchFlow = null,
        GameFlow? gameFlow = null)

    {
        this.codec = codec ?? new ServerProtocolCodec();

        this.variant = variant ?? new ClassicLordVariant();

        this.enterMatchFlow = enterMatchFlow;

        this.gameFlow = gameFlow ?? new GameFlow(this.codec);
    }


    /// <summary>
    /// Runs the scenario on a dedicated loop thread via <see cref="CRpcLoopRunner"/>.
    /// </summary>
    public static ScenarioReport RunHosted(
        LordUnionTestConfig config,
        ScenarioRunOptions options,
        ServerProtocolCodec? codec = null,
        CancellationToken cancellationToken = default)

    {
        var loop = new CRpcLoop();

        return CRpcLoopRunner.RunUntilComplete(
            loop,
            () => new ThreePlayersOneGameScenario(codec).RunAsync(loop, config, options, cancellationToken));
    }


    /// <summary>
    /// Must be invoked on the owner <paramref name="loop"/> thread (typically inside
    /// <see cref="CRpcLoopRunner.RunUntilComplete"/>). IO transports post ingress back to this loop.
    /// </summary>
    public CRpcTask<ScenarioReport> RunAsync(
        CRpcLoop loop,
        LordUnionTestConfig config,
        ScenarioRunOptions options,
        CancellationToken cancellationToken = default)

    {
        ArgumentNullException.ThrowIfNull(loop);

        ArgumentNullException.ThrowIfNull(config);

        ArgumentNullException.ThrowIfNull(options);


        return RunCoreAsync(loop, config, options, cancellationToken);
    }


    private async CRpcTask<ScenarioReport> RunCoreAsync(
        CRpcLoop loop,
        LordUnionTestConfig config,
        ScenarioRunOptions options,
        CancellationToken cancellationToken)

    {
        EnsureOnLoopThread(loop);


        if (config.Accounts.Count < 3)

        {
            throw new InvalidOperationException(
                $"ThreePlayersOneGameScenario requires at least 3 accounts; got {config.Accounts.Count}.");
        }


        var factory = ResolveTransportFactory(options);

        var bundles = config.Accounts
            .Take(3)
            .Select(account => CreateBundle(loop, account, factory))
            .ToList();

        var profile = LordUnionGameProfiles.FromConfig(config.Match, variant);


        var timings = bundles.ToDictionary(
            bundle => bundle.Session.Alias,
            bundle => bundle.Timing);


        cancellationToken.ThrowIfCancellationRequested();


        var loginResults = await RunPhaseConcurrentOnLoopAsync(
            bundles,
            bundle => RunLoginAsync(bundle, config, cancellationToken),
            static (timing, elapsed) => timing.LoginDuration = elapsed);


        var loginFailure = FindLoginFailure(loginResults);

        if (loginFailure is not null)

        {
            return BuildFailureReport(timings, loginFailure);
        }

        if (!options.SkipAccountCleanup)

        {
            await RunPhaseConcurrentOnLoopAsync(
                bundles,
                bundle => bundle.Client.CleanupAsync(config.Match),
                static (_, _) => { });
        }

        var signupResults = await RunPhaseConcurrentOnLoopAsync(
            bundles,
            bundle => RunSignupAsync(bundle, config, profile, cancellationToken),
            static (timing, elapsed) => timing.SignupDuration = elapsed);


        var signupFailure = FindSignupFailure(signupResults);

        if (signupFailure is not null)

        {
            return BuildFailureReport(timings, signupFailure);
        }

        if (!AllSignupsSucceeded(signupResults, out var signupGateFailure))

        {
            return BuildFailureReport(timings, signupGateFailure!);
        }

        var postSignupMonitors = PostSignupDiagnosticMonitor.Install(
            signupResults
                .Where(result => result.Result is not null)
                .Select(result => (result.Bundle.Session, result.Result!)));


        var enterResults = await RunEnterMatchPhaseAsync(
            bundles,
            config,
            profile,
            options,
            postSignupMonitors,
            cancellationToken);


        var enterFailure = FindEnterMatchFailure(enterResults);

        if (enterFailure is not null)

        {
            return BuildFailureReport(timings, enterFailure, postSignupMonitors);
        }


        var successfulEnterResults = enterResults
            .Select(result => result.Result!)
            .ToList();


        try

        {
            SameTableVerifier.Verify(successfulEnterResults);
        }

        catch (Exception ex)

        {
            return BuildFailureReport(
                timings,
                ScenarioFailureDetail.FromSession(
                    bundles[0].Session,
                    ex.Message,
                    exception: ex),
                postSignupMonitors);
        }


        var gameResults = await RunPhaseConcurrentOnLoopAsync(
            bundles,
            bundle => RunGameAsync(bundle, config, options, cancellationToken),
            static (timing, elapsed) => timing.GameDuration = elapsed);


        var gameFailure = FindFirstGameFailure(gameResults);

        if (gameFailure is not null)

        {
            return BuildFailureReport(timings, gameFailure, postSignupMonitors);
        }


        var referenceEnter = successfulEnterResults[0];

        IReadOnlyList<AccountCleanupSummary> postGameCleanupSummaries = Array.Empty<AccountCleanupSummary>();

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
                .ToList();
        }

        return new ScenarioReport

        {
            Success = true,

            AccountTimings = ToTimings(timings),

            MatchId = referenceEnter.MatchId,

            TableId = referenceEnter.TableId ?? referenceEnter.MatchId,

            SeatUserMapping = referenceEnter.SeatUserMapping,

            PostGameCleanupSummaries = postGameCleanupSummaries,

            GameEndSummaries = gameResults
                .Select(result => new AccountGameEndSummary
                {
                    AccountAlias = result.Bundle.Session.Alias,
                    GameFlowWinSeat = result.Result?.WinSeat,
                    EndSignal = result.Result?.EndSignal,
                })
                .ToList(),

            WinSeat = ScenarioWinSeatResolver.ResolveAggregateWinSeat(
                gameResults.Select(result => result.Result?.WinSeat)),

            SignupDiagnostics = CreateSignupDiagnosticSnapshots(postSignupMonitors),
        };
    }


    private async CRpcTask<LoginFlowResult> RunLoginAsync(
        AccountBundle bundle,
        LordUnionTestConfig config,
        CancellationToken cancellationToken)

    {
        _ = cancellationToken;

        try
        {
            await bundle.Client.ConnectAsync(config.Server, config.Timeouts.ConnectTimeout);

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
        }
        catch (InvalidOperationException)
        {
            var loginErrorCode = bundle.Session.LoginErrorCode ?? 0;
            return new LoginFlowResult
            {
                Success = false,
                UserId = bundle.Session.UserId,
                Nickname = bundle.Session.Nickname,
                AesKey = bundle.Session.AesKey,
                SessionId = bundle.Session.SessionId,
                LoginErrorCode = loginErrorCode,
                AnonymousRouteId = bundle.Session.AnonymousRouteId ?? 0,
                LoginRouteId = bundle.Session.LoginRouteId ?? 0,
                FailureMessage = $"Login failed with error code {loginErrorCode}. ackJson=",
            };
        }
    }


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


    private async CRpcTask<SignupFlowResult> RunSignupAsync(
        AccountBundle bundle,
        LordUnionTestConfig config,
        LordUnionGameProfile profile,
        CancellationToken cancellationToken)

    {
        _ = cancellationToken;

        try
        {
            var signup = await bundle.Client.SignupAsync(profile, config.Timeouts.SignupTimeout);

            return new SignupFlowResult
            {
                Success = true,
                SignupErrorCode = signup.SignupAckParam,
                MobileAckParam = (uint)signup.MobileResult,
                Flags = 0,
                TourneyId = signup.TourneyId,
                MatchPoint = signup.MatchPoint,
                GameId = (int)signup.GameId,
                FailureMessage = null,
            };
        }
        catch (InvalidOperationException ex)
        {
            var signupErrorCode = TryParseSignupErrorCode(ex.Message);
            return new SignupFlowResult
            {
                Success = false,
                SignupErrorCode = signupErrorCode,
                MobileAckParam = signupErrorCode,
                FailureMessage = $"Tourney signup failed with error code {signupErrorCode}.",
            };
        }
    }

    private static uint TryParseSignupErrorCode(string message)
    {
        var signupAckMatch = Regex.Match(
            message,
            @"TourneySignupAck param=(\d+)",
            RegexOptions.CultureInvariant);
        if (signupAckMatch.Success
            && uint.TryParse(signupAckMatch.Groups[1].Value, out var signupAckCode)
            && signupAckCode != 0)
        {
            return signupAckCode;
        }

        var mobileMatch = Regex.Match(message, @"mobile\.param=(\d+)", RegexOptions.CultureInvariant);
        if (mobileMatch.Success && uint.TryParse(mobileMatch.Groups[1].Value, out var mobileCode) && mobileCode != 0)
        {
            return mobileCode;
        }

        var match = Regex.Match(message, @"param=(\d+)", RegexOptions.CultureInvariant);
        return match.Success && uint.TryParse(match.Groups[1].Value, out var errorCode)
            ? errorCode
            : 0;
    }


    private async CRpcTask<List<PhaseResult<EnterMatchFlowResult>>> RunEnterMatchPhaseAsync(
        IReadOnlyList<AccountBundle> bundles,
        LordUnionTestConfig config,
        LordUnionGameProfile profile,
        ScenarioRunOptions options,
        IReadOnlyList<PostSignupDiagnosticMonitor> postSignupMonitors,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var monitorsByAlias = postSignupMonitors.ToDictionary(monitor => monitor.AccountAlias);

        if (options.MatchStartAckFactory is not null)
        {
            return await RunPhaseConcurrentOnLoopAsync(
                bundles,
                bundle => RunEnterMatchAsync(bundle, config, profile, options, cancellationToken),
                static (timing, elapsed) => timing.EnterMatchDuration = elapsed);
        }

        var phaseStopwatch = Stopwatch.StartNew();

        foreach (var bundle in bundles)
        {
            if (monitorsByAlias.TryGetValue(bundle.Session.Alias, out var monitor))
            {
                bundle.Client.ImportPostSignupMonitor(monitor);
            }
        }

        var startResults = await RunPhaseConcurrentOnLoopAsync(
            bundles,
            bundle => bundle.Client.WaitForMatchStartAsync(config.Timeouts.MatchStartTimeout),
            null);

        if (FindMatchStartFailure(startResults) is not null)
        {
            foreach (var bundle in bundles)
            {
                bundle.Timing.EnterMatchDuration = phaseStopwatch.Elapsed;
            }

            return startResults.Select(result => new PhaseResult<EnterMatchFlowResult>(
                result.Bundle,
                null,
                result.Exception)).ToList();
        }

        var matchStartByAlias = startResults.ToDictionary(
            result => result.Bundle.Session.Alias,
            result => result.Result!);

        var enterMatchResults = await RunPhaseConcurrentOnLoopAsync(
            bundles,
            bundle => bundle.Client.EnterMatchAsync(
                profile,
                matchStartByAlias[bundle.Session.Alias],
                config.Timeouts.EnterMatchTimeout),
            null);

        if (FindEnterMatchOnlyFailure(enterMatchResults) is not null)
        {
            foreach (var bundle in bundles)
            {
                bundle.Timing.EnterMatchDuration = phaseStopwatch.Elapsed;
            }

            return enterMatchResults.Select(result => new PhaseResult<EnterMatchFlowResult>(
                result.Bundle,
                null,
                result.Exception)).ToList();
        }

        var enterResults = await RunPhaseConcurrentOnLoopAsync(
            bundles,
            async bundle =>
            {
                var enterRound = await bundle.Client.EnterRoundAsync(
                    profile,
                    config.Timeouts.EnterRoundTimeout);
                return MapEnterRoundResult(bundle, profile, enterRound);
            },
            null);

        foreach (var bundle in bundles)
        {
            bundle.Timing.EnterMatchDuration = phaseStopwatch.Elapsed;
        }

        return enterResults;
    }

    private async CRpcTask<EnterMatchFlowResult> RunEnterMatchAsync(
        AccountBundle bundle,
        LordUnionTestConfig config,
        LordUnionGameProfile profile,
        ScenarioRunOptions options,
        CancellationToken cancellationToken)

    {
        _ = cancellationToken;

        if (options.MatchStartAckFactory is not null
            && bundle.Transport is FakeGameServerTransport fakeTransport)

        {
            var matchStartTask = bundle.Client.WaitForMatchStartAsync(config.Timeouts.MatchStartTimeout);

            fakeTransport.DeliverIncomingMessage(options.MatchStartAckFactory(bundle.Session));

            var matchStart = await matchStartTask;

            await bundle.Client.EnterMatchAsync(
                profile,
                matchStart,
                config.Timeouts.EnterMatchTimeout);

            var enterRound = await bundle.Client.EnterRoundAsync(
                profile,
                config.Timeouts.EnterRoundTimeout);

            return MapEnterRoundResult(bundle, profile, enterRound);
        }

        var matchStartInfo = await bundle.Client.WaitForMatchStartAsync(config.Timeouts.MatchStartTimeout);

        await bundle.Client.EnterMatchAsync(
            profile,
            matchStartInfo,
            config.Timeouts.EnterMatchTimeout);

        var enterRoundResult = await bundle.Client.EnterRoundAsync(
            profile,
            config.Timeouts.EnterRoundTimeout);

        return MapEnterRoundResult(bundle, profile, enterRoundResult);
    }

    private static EnterMatchFlowResult MapEnterRoundResult(
        AccountBundle bundle,
        LordUnionGameProfile profile,
        EnterRoundStageResult enterRound)
    {
        return new EnterMatchFlowResult
        {
            Success = true,
            UserId = bundle.Session.UserId,
            MatchId = enterRound.MatchId,
            TableId = enterRound.TableId,
            SeatOrder = enterRound.Seat,
            TourneyId = profile.TourneyId,
            MatchPoint = profile.MatchPoint,
            GameId = profile.GameId,
            Ticket = bundle.Session.Ticket ?? Array.Empty<byte>(),
            FailureMessage = null,
        };
    }


    private async CRpcTask<GameFlowResult> RunGameAsync(
        AccountBundle bundle,
        LordUnionTestConfig config,
        ScenarioRunOptions options,
        CancellationToken cancellationToken)

    {
        _ = cancellationToken;

        var bot = new MinimalLandlordBot();

        var policy = options.PolicyOverride ?? new MinimalLandlordBotPolicy(bot);
        var scheduler = ActionSchedulerFactory.Create(config.Bot, config.Timeouts, options);


        if (options.GameFlowOverride is not null)

        {
            return await options.GameFlowOverride(
                bundle.Session,
                bot,
                variant,
                bundle.Transport,
                config.Timeouts.GameOverTimeout);
        }


        return await gameFlow.RunUntilFinishedAsync(
            bundle.Session,
            policy,
            variant,
            config.Timeouts.GameOverTimeout,
            scheduler,
            bundle.Transport);
    }


    private static IScenarioTransportFactory ResolveTransportFactory(ScenarioRunOptions options)

    {
        if (options.TransportFactory is not null)

        {
            return options.TransportFactory;
        }


        if (options.UseLiveTransport)

        {
            return new LiveScenarioTransportFactory();
        }


        throw new InvalidOperationException(
            "ScenarioRunOptions must specify TransportFactory for fake runs or UseLiveTransport for live runs.");
    }


    private AccountBundle CreateBundle(
        CRpcLoop loop,
        AccountConfig account,
        IScenarioTransportFactory factory)

    {
        var session = new AccountSession(loop, account.Alias, codec);

        var transport = factory.CreateTransport(session, account);

        var client = new LordUnionSessionClient(session, transport, codec, enterMatchFlow);


        return new AccountBundle

        {
            Loop = loop,

            Session = session,

            Transport = transport,

            Client = client,

            Account = account,

            Timing = new MutableAccountTiming(),
        };
    }


    /// <summary>
    /// Starts one async flow per account (each runs until its first await), then awaits all on the
    /// shared loop thread so login/signup/enter/game requests overlap without extra threads.
    /// </summary>
    private static async CRpcTask<List<PhaseResult<T>>> RunPhaseConcurrentOnLoopAsync<T>(
        IReadOnlyList<AccountBundle> bundles,
        Func<AccountBundle, CRpcTask<T>> work,
        Action<MutableAccountTiming, TimeSpan>? recordDuration = null)

    {
        var pending = bundles
            .Select(bundle => (Bundle: bundle, Task: work(bundle), Stopwatch: Stopwatch.StartNew()))
            .ToList();


        var results = new PhaseResult<T>[pending.Count];

        for (var index = 0; index < pending.Count; index++)

        {
            var entry = pending[index];

            try

            {
                var value = await entry.Task;

                recordDuration?.Invoke(entry.Bundle.Timing, entry.Stopwatch.Elapsed);

                results[index] = new PhaseResult<T>(entry.Bundle, value, null);
            }

            catch (Exception ex)

            {
                recordDuration?.Invoke(entry.Bundle.Timing, entry.Stopwatch.Elapsed);

                results[index] = new PhaseResult<T>(entry.Bundle, default, ex);
            }
        }


        return results.ToList();
    }


    private static async CRpcTask<List<PhaseResult<T>>> RunPhaseSequentialOnLoopAsync<T>(
        IReadOnlyList<AccountBundle> bundles,
        Func<AccountBundle, CRpcTask<T>> work,
        Action<MutableAccountTiming, TimeSpan>? recordDuration = null)

    {
        var results = new List<PhaseResult<T>>(bundles.Count);

        foreach (var bundle in bundles)

        {
            var stopwatch = Stopwatch.StartNew();

            try

            {
                var value = await work(bundle);

                recordDuration?.Invoke(bundle.Timing, stopwatch.Elapsed);

                results.Add(new PhaseResult<T>(bundle, value, null));
            }

            catch (Exception ex)

            {
                recordDuration?.Invoke(bundle.Timing, stopwatch.Elapsed);

                results.Add(new PhaseResult<T>(bundle, default, ex));
            }
        }


        return results;
    }


    private static ScenarioFailureDetail? FindLoginFailure(IReadOnlyList<PhaseResult<LoginFlowResult>> results) =>
        FindFlowFailure(results, "Login failed.");


    private static ScenarioFailureDetail? FindSignupFailure(IReadOnlyList<PhaseResult<SignupFlowResult>> results) =>
        FindFlowFailure(results, "Signup failed.");


    private static bool AllSignupsSucceeded(
        IReadOnlyList<PhaseResult<SignupFlowResult>> results,
        out ScenarioFailureDetail? failure)
    {
        foreach (var phaseResult in results)
        {
            if (phaseResult.Result is not SignupFlowResult signup || !signup.Success)
            {
                failure = ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    phaseResult.Result?.FailureMessage ?? "Signup result missing or unsuccessful.");
                return false;
            }

            if (phaseResult.Bundle.Session.State != AccountSessionState.SignedUp)
            {
                failure = ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    $"Signup reported success but session state is {phaseResult.Bundle.Session.State}.");
                return false;
            }
        }

        failure = null;
        return true;
    }


    private static ScenarioFailureDetail? FindEnterMatchFailure(
        IReadOnlyList<PhaseResult<EnterMatchFlowResult>> results) =>
        FindFlowFailure(results, "Enter match failed.");


    private static ScenarioFailureDetail? FindMatchStartFailure(
        IReadOnlyList<PhaseResult<MatchStartStageResult>> results)
    {
        foreach (var phaseResult in results)
        {
            if (phaseResult.Exception is TimeoutException timeoutException)
            {
                return ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    timeoutException.Message,
                    timeoutName: timeoutException.Message,
                    exception: timeoutException);
            }

            if (phaseResult.Exception is not null)
            {
                return ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    phaseResult.Exception.Message,
                    exception: phaseResult.Exception);
            }
        }

        return null;
    }


    private static ScenarioFailureDetail? FindEnterMatchOnlyFailure(
        IReadOnlyList<PhaseResult<EnterMatchStageResult>> results)
    {
        foreach (var phaseResult in results)
        {
            if (phaseResult.Exception is TimeoutException timeoutException)
            {
                return ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    timeoutException.Message,
                    timeoutName: timeoutException.Message,
                    exception: timeoutException);
            }

            if (phaseResult.Exception is not null)
            {
                return ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    phaseResult.Exception.Message,
                    exception: phaseResult.Exception);
            }
        }

        return null;
    }


    private static ScenarioFailureDetail? FindFlowFailure<T>(
        IReadOnlyList<PhaseResult<T>> results,
        string defaultMessage)
        where T : class

    {
        foreach (var phaseResult in results)

        {
            if (phaseResult.Exception is TimeoutException timeoutException)

            {
                return ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    timeoutException.Message,
                    timeoutName: timeoutException.Message,
                    exception: timeoutException);
            }


            if (phaseResult.Exception is not null)

            {
                return ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    phaseResult.Exception.Message,
                    exception: phaseResult.Exception);
            }


            string? failure = null;

            if (phaseResult.Result is LoginFlowResult login && !login.Success)

            {
                failure = login.FailureMessage ?? defaultMessage;
            }

            else if (phaseResult.Result is SignupFlowResult signup && !signup.Success)

            {
                failure = signup.FailureMessage ?? defaultMessage;
            }

            else if (phaseResult.Result is EnterMatchFlowResult enter && !enter.Success)

            {
                failure = enter.FailureMessage ?? defaultMessage;
            }


            if (failure is not null)

            {
                return ScenarioFailureDetail.FromSession(phaseResult.Bundle.Session, failure);
            }
        }


        return null;
    }


    private static ScenarioFailureDetail? FindFirstGameFailure(
        IReadOnlyList<PhaseResult<GameFlowResult>> results)

    {
        foreach (var phaseResult in results)

        {
            if (phaseResult.Exception is TimeoutException timeoutException)

            {
                return ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    timeoutException.Message,
                    timeoutName: timeoutException.Message,
                    exception: timeoutException);
            }


            if (phaseResult.Exception is not null)

            {
                return ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    phaseResult.Exception.Message,
                    exception: phaseResult.Exception);
            }


            if (phaseResult.Result is { Success: false })

            {
                return ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    phaseResult.Result.FailureMessage ?? "Game flow failed.");
            }
        }


        return null;
    }


    private static ScenarioReport BuildFailureReport(
        IReadOnlyDictionary<string, MutableAccountTiming> timings,
        ScenarioFailureDetail failure,
        IReadOnlyList<PostSignupDiagnosticMonitor>? postSignupMonitors = null)

    {
        return new ScenarioReport

        {
            Success = false,

            AccountTimings = ToTimings(timings),

            FirstFailure = failure,

            SignupDiagnostics = CreateSignupDiagnosticSnapshots(postSignupMonitors),
        };
    }


    private static IReadOnlyList<SignupDiagnosticSnapshot> CreateSignupDiagnosticSnapshots(
        IReadOnlyList<PostSignupDiagnosticMonitor>? monitors)
    {
        if (monitors is null || monitors.Count == 0)
        {
            return Array.Empty<SignupDiagnosticSnapshot>();
        }

        return monitors.Select(monitor => monitor.CreateSnapshot()).ToList();
    }


    private static IReadOnlyList<AccountPhaseTiming> ToTimings(
        IReadOnlyDictionary<string, MutableAccountTiming> timings)

    {
        return timings
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new AccountPhaseTiming

            {
                AccountAlias = entry.Key,

                ConnectDuration = entry.Value.ConnectDuration,

                LoginDuration = entry.Value.LoginDuration,

                SignupDuration = entry.Value.SignupDuration,

                EnterMatchDuration = entry.Value.EnterMatchDuration,

                GameDuration = entry.Value.GameDuration,
            })
            .ToList();
    }


    private static void EnsureOnLoopThread(CRpcLoop loop)

    {
        if (!loop.IsInLoopThread)

        {
            throw new InvalidOperationException(
                "ThreePlayersOneGameScenario must run on its shared CRpcLoop thread.");
        }
    }


    private sealed class AccountBundle

    {
        public required CRpcLoop Loop { get; init; }


        public required AccountSession Session { get; init; }


        public required IGameServerTransport Transport { get; init; }


        public required LordUnionSessionClient Client { get; init; }


        public required AccountConfig Account { get; init; }


        public required MutableAccountTiming Timing { get; init; }
    }


    private sealed class MutableAccountTiming

    {
        public TimeSpan ConnectDuration { get; set; }


        public TimeSpan LoginDuration { get; set; }


        public TimeSpan SignupDuration { get; set; }


        public TimeSpan EnterMatchDuration { get; set; }


        public TimeSpan GameDuration { get; set; }
    }


    private sealed record PhaseResult<T>(AccountBundle Bundle, T? Result, Exception? Exception);
}

public sealed class LiveScenarioTransportFactory : IScenarioTransportFactory

{
    private readonly ServerProtocolCodec codec;


    public LiveScenarioTransportFactory(ServerProtocolCodec? codec = null)

    {
        this.codec = codec ?? new ServerProtocolCodec();
    }


    public IGameServerTransport CreateTransport(AccountSession session, AccountConfig account)

    {
        _ = account;

        return new GameServerDotNettyTransport(codec);
    }
}