using System.Diagnostics;
using CRpc.Async;
using DotNetty.Transport.Channels;
using LordUnion.IntegrationTests.Bots;
using LordUnion.IntegrationTests.Bots.Pacing;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Games.TKLord.Replay;
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

    private readonly ILordGameVariant variant;


    public ThreePlayersOneGameScenario(
        ServerProtocolCodec? codec = null,
        ILordGameVariant? variant = null)
    {
        this.codec = codec ?? new ServerProtocolCodec();

        this.variant = variant ?? new ClassicLordVariant();
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

        LordUnionSharedIo? sharedIo = null;
        List<AccountBundle>? bundles = null;

        try
        {
            if (options.TransportFactory is null && options.UseLiveTransport)
            {
                sharedIo = LordUnionSharedIo.FromConfig(config);
            }

            var factory = ResolveTransportFactory(options, sharedIo);

            bundles = config.Accounts
                .Take(3)
                .Select(account => CreateBundle(loop, account, factory))
                .ToList();

            return await RunCoreWithBundlesAsync(
                loop,
                config,
                options,
                cancellationToken,
                bundles);
        }
        finally
        {
            if (bundles is not null)
            {
                await DisposeBundleTransportsAsync(loop, bundles);
            }

            if (sharedIo is not null)
            {
                await sharedIo.DisposeAsync(loop);
            }
        }
    }

    private async CRpcTask<ScenarioReport> RunCoreWithBundlesAsync(
        CRpcLoop loop,
        LordUnionTestConfig config,
        ScenarioRunOptions options,
        CancellationToken cancellationToken,
        List<AccountBundle> bundles)

    {
        _ = loop;

        var profile = LordUnionGameProfiles.FromConfig(config.Match, variant);


        var timings = bundles.ToDictionary(
            bundle => bundle.Session.Alias,
            bundle => bundle.Timing);


        cancellationToken.ThrowIfCancellationRequested();


        var loginResults = await RunPhaseConcurrentOnLoopAsync(
            bundles,
            bundle => RunLoginPhaseAsync(bundle, config, cancellationToken),
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
            bundle => RunSignupPhaseAsync(bundle, profile, config, cancellationToken),
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


        var xmlReplayCoordinator = options.XmlReplayCoordinator
            ?? new XmlReplayCoordinator(AppContext.BaseDirectory);
        var gameOptions = options.XmlReplayCoordinator is null
            ? CopyOptionsWithXmlReplayCoordinator(options, xmlReplayCoordinator)
            : options;
        gameOptions = CopyOptionsWithTableGamePhase(gameOptions, new TableGamePhaseCoordinator());

        var gameResults = await RunPhaseConcurrentOnLoopAsync(
            bundles,
            bundle => RunGameAsync(bundle, profile, config, gameOptions, cancellationToken),
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

            TableId = referenceEnter.TableId,

            SeatUserMapping = referenceEnter.SeatUserMapping,

            PostGameCleanupSummaries = postGameCleanupSummaries,

            GameEndSummaries = gameResults
                .Select(result => new AccountGameEndSummary
                {
                    AccountAlias = result.Bundle.Session.Alias,
                    WinSeat = result.Result?.WinSeat,
                    EndSignal = result.Result?.EndSignal,
                })
                .ToList(),

            WinSeat = ScenarioWinSeatResolver.ResolveAggregateWinSeat(
                gameResults.Select(result => result.Result?.WinSeat)),

            SignupDiagnostics = CreateSignupDiagnosticSnapshots(postSignupMonitors),
        };
    }

    private static async CRpcTask DisposeBundleTransportsAsync(
        CRpcLoop loop,
        IReadOnlyList<AccountBundle> bundles)
    {
        foreach (var bundle in bundles)
        {
            await bundle.Transport.DisconnectAsync(loop);

            if (bundle.Transport is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }


    private async CRpcTask<LoginStageResult> RunLoginPhaseAsync(
        AccountBundle bundle,
        LordUnionTestConfig config,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        try
        {
            await bundle.Client.ConnectAsync(config.Server, config.Timeouts.ConnectTimeout);
            return await bundle.Client.LoginAsync(
                bundle.Account,
                config.Protocol,
                config.Timeouts.LoginTimeout);
        }
        catch (InvalidOperationException)
        {
            return ScenarioStageMapping.FromLoginFailure(bundle.Session);
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


    private async CRpcTask<SignupStageResult> RunSignupPhaseAsync(
        AccountBundle bundle,
        LordUnionGameProfile profile,
        LordUnionTestConfig config,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        try
        {
            return await bundle.Client.SignupAsync(profile, config.Timeouts.SignupTimeout);
        }
        catch (InvalidOperationException ex)
        {
            return ScenarioStageMapping.FromSignupFailure(profile, ex);
        }
    }

    private async CRpcTask<List<PhaseResult<EnterTableStageResult>>> RunEnterMatchPhaseAsync(
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

            return startResults.Select(result => new PhaseResult<EnterTableStageResult>(
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

            return enterMatchResults.Select(result => new PhaseResult<EnterTableStageResult>(
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
                return bundle.Client.ToEnterTableStageResult(profile, enterRound);
            },
            null);

        foreach (var bundle in bundles)
        {
            bundle.Timing.EnterMatchDuration = phaseStopwatch.Elapsed;
        }

        return enterResults;
    }

    private async CRpcTask<EnterTableStageResult> RunEnterMatchAsync(
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
            await bundle.Client.EnterMatchAsync(profile, matchStart, config.Timeouts.EnterMatchTimeout);
            var enterRound = await bundle.Client.EnterRoundAsync(profile, config.Timeouts.EnterRoundTimeout);
            return bundle.Client.ToEnterTableStageResult(profile, enterRound);
        }

        return await bundle.Client.EnterTableAsync(profile, config.Timeouts);
    }


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
            var bot = new MinimalLandlordBot();
            var policy = options.PolicyOverride ?? new MinimalLandlordBotPolicy(bot);
            var scheduler = ActionSchedulerFactory.Create(config.Bot, config.Timeouts, options);
            return await options.PlayGameOverride(
                bundle.Client,
                profile,
                policy,
                scheduler,
                config.Timeouts.GameOverTimeout);
        }

        if (options.XmlReplayCoordinator is { } coordinator)
        {
            if (bundle.Client.Session.SeatOrder is not uint seat)
            {
                throw new InvalidOperationException(
                    $"Account '{bundle.Client.Session.Alias}' has no resolved seat before game flow.");
            }

            var policy = coordinator.CreatePolicy(seat);
            return await bundle.Client.PlayGameAsync(
                profile,
                policy,
                ImmediateActionScheduler.Instance,
                config.Timeouts.GameOverTimeout,
                options.TableGamePhaseCoordinator);
        }

        return await bundle.Client.PlayGameAsync(profile, config, options);
    }

    private static ScenarioRunOptions CopyOptionsWithXmlReplayCoordinator(
        ScenarioRunOptions options,
        XmlReplayCoordinator coordinator) =>
        new()
        {
            UseLiveTransport = options.UseLiveTransport,
            SkipAccountCleanup = options.SkipAccountCleanup,
            SkipBotPacing = options.SkipBotPacing,
            PolicyOverride = options.PolicyOverride,
            SchedulerOverride = options.SchedulerOverride,
            TransportFactory = options.TransportFactory,
            PlayGameOverride = options.PlayGameOverride,
            MatchStartAckFactory = options.MatchStartAckFactory,
            XmlReplayCoordinator = coordinator,
            TableGamePhaseCoordinator = options.TableGamePhaseCoordinator,
        };

    private static ScenarioRunOptions CopyOptionsWithTableGamePhase(
        ScenarioRunOptions options,
        TableGamePhaseCoordinator tableGamePhase) =>
        new()
        {
            UseLiveTransport = options.UseLiveTransport,
            SkipAccountCleanup = options.SkipAccountCleanup,
            SkipBotPacing = options.SkipBotPacing,
            PolicyOverride = options.PolicyOverride,
            SchedulerOverride = options.SchedulerOverride,
            TransportFactory = options.TransportFactory,
            PlayGameOverride = options.PlayGameOverride,
            MatchStartAckFactory = options.MatchStartAckFactory,
            XmlReplayCoordinator = options.XmlReplayCoordinator,
            TableGamePhaseCoordinator = tableGamePhase,
        };


    private IScenarioTransportFactory ResolveTransportFactory(
        ScenarioRunOptions options,
        LordUnionSharedIo? sharedIo)

    {
        if (options.TransportFactory is not null)

        {
            return options.TransportFactory;
        }


        if (options.UseLiveTransport)

        {
            if (sharedIo is null)
            {
                throw new InvalidOperationException(
                    "Live transport requires a LordUnionSharedIo instance for the scenario run.");
            }

            return new LiveScenarioTransportFactory(codec, sharedIo.EventLoopGroup);
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

        var client = new LordUnionSessionClient(session, transport, codec);


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


    private static ScenarioFailureDetail? FindLoginFailure(IReadOnlyList<PhaseResult<LoginStageResult>> results) =>
        FindFlowFailure(results, "Login failed.");


    private static ScenarioFailureDetail? FindSignupFailure(IReadOnlyList<PhaseResult<SignupStageResult>> results) =>
        FindFlowFailure(results, "Signup failed.");


    private static bool AllSignupsSucceeded(
        IReadOnlyList<PhaseResult<SignupStageResult>> results,
        out ScenarioFailureDetail? failure)
    {
        foreach (var phaseResult in results)
        {
            if (phaseResult.Result is not SignupStageResult signup || !signup.Success)
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
        IReadOnlyList<PhaseResult<EnterTableStageResult>> results) =>
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

            if (phaseResult.Result is LoginStageResult login && !login.Success)

            {
                failure = login.FailureMessage ?? defaultMessage;
            }

            else if (phaseResult.Result is SignupStageResult signup && !signup.Success)

            {
                failure = signup.FailureMessage ?? defaultMessage;
            }


            if (failure is not null)

            {
                return ScenarioFailureDetail.FromSession(phaseResult.Bundle.Session, failure);
            }
        }


        return null;
    }


    private static ScenarioFailureDetail? FindFirstGameFailure(
        IReadOnlyList<PhaseResult<GameStageResult>> results)

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


            if (phaseResult.Exception is FileNotFoundException fileNotFoundException)

            {
                return ScenarioFailureDetail.FromSession(
                    phaseResult.Bundle.Session,
                    fileNotFoundException.Message,
                    exception: fileNotFoundException,
                    fixturePath: fileNotFoundException.FileName);
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
    private readonly IEventLoopGroup sharedEventLoopGroup;


    public LiveScenarioTransportFactory(
        ServerProtocolCodec? codec,
        IEventLoopGroup sharedEventLoopGroup)

    {
        this.codec = codec ?? new ServerProtocolCodec();
        this.sharedEventLoopGroup = sharedEventLoopGroup
                                     ?? throw new ArgumentNullException(nameof(sharedEventLoopGroup));
    }


    public IGameServerTransport CreateTransport(AccountSession session, AccountConfig account)

    {
        _ = account;

        return new GameServerDotNettyTransport(codec, sharedEventLoopGroup);
    }
}