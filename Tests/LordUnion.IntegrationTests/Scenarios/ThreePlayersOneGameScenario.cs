using System.Diagnostics;

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

    private readonly LoginFlow loginFlow;

    private readonly AccountCleanupFlow accountCleanupFlow;

    private readonly SignupFlow signupFlow;

    private readonly EnterMatchFlow enterMatchFlow;

    private readonly GameFlow gameFlow;

    private readonly ILordGameVariant variant;



    public ThreePlayersOneGameScenario(

        ServerProtocolCodec? codec = null,

        ILordGameVariant? variant = null,

        LoginFlow? loginFlow = null,

        AccountCleanupFlow? accountCleanupFlow = null,

        SignupFlow? signupFlow = null,

        EnterMatchFlow? enterMatchFlow = null,

        GameFlow? gameFlow = null)

    {

        this.codec = codec ?? new ServerProtocolCodec();

        this.variant = variant ?? new ClassicLordVariant();

        this.loginFlow = loginFlow ?? new LoginFlow(this.codec);

        this.accountCleanupFlow = accountCleanupFlow ?? new AccountCleanupFlow(this.codec);

        this.signupFlow = signupFlow ?? new SignupFlow(this.codec);

        this.enterMatchFlow = enterMatchFlow ?? new EnterMatchFlow(this.codec);

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

                bundle => RunAccountCleanupAsync(bundle, config, cancellationToken),

                static (_, _) => { });

        }

        var matchProgressStates = bundles.ToDictionary(
            bundle => bundle.Session.Alias,
            _ => new EnterMatchFlowSessionState());

        foreach (var bundle in bundles)
        {
            EnterMatchFlow.InstallMatchProgressCapture(bundle.Session, matchProgressStates[bundle.Session.Alias]);
        }



        var signupResults = await RunPhaseConcurrentOnLoopAsync(

            bundles,

            bundle => RunSignupAsync(bundle, config, matchProgressStates[bundle.Session.Alias], cancellationToken),

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
            options,
            postSignupMonitors,
            matchProgressStates,
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

        return new ScenarioReport

        {

            Success = true,

            AccountTimings = ToTimings(timings),

            MatchId = referenceEnter.MatchId,

            TableId = referenceEnter.TableId ?? referenceEnter.MatchId,

            SeatUserMapping = referenceEnter.SeatUserMapping,

            WinSeat = gameResults.Select(result => result.Result?.WinSeat).FirstOrDefault(seat => seat is > 0),

            SignupDiagnostics = CreateSignupDiagnosticSnapshots(postSignupMonitors),

        };

    }



    private async CRpcTask<LoginFlowResult> RunLoginAsync(

        AccountBundle bundle,

        LordUnionTestConfig config,

        CancellationToken cancellationToken)

    {

        _ = cancellationToken;



        return await loginFlow.RunAsync(

            bundle.Session,

            bundle.Account,

            config.Server,

            config.Protocol,

            config.Timeouts.LoginTimeout,

            bundle.Transport);

    }



    private async CRpcTask<AccountCleanupFlowResult> RunAccountCleanupAsync(

        AccountBundle bundle,

        LordUnionTestConfig config,

        CancellationToken cancellationToken)

    {

        _ = cancellationToken;

        return await accountCleanupFlow.RunAsync(

            bundle.Session,

            config.Match,

            bundle.Transport);

    }



    private async CRpcTask<SignupFlowResult> RunSignupAsync(

        AccountBundle bundle,

        LordUnionTestConfig config,

        EnterMatchFlowSessionState matchProgressState,

        CancellationToken cancellationToken)

    {

        _ = cancellationToken;

        return await signupFlow.RunAsync(

            bundle.Session,

            config.Match,

            config.Timeouts.SignupTimeout,

            bundle.Transport,

            matchProgressState);

    }



    private async CRpcTask<List<PhaseResult<EnterMatchFlowResult>>> RunEnterMatchPhaseAsync(
        IReadOnlyList<AccountBundle> bundles,
        LordUnionTestConfig config,
        ScenarioRunOptions options,
        IReadOnlyList<PostSignupDiagnosticMonitor> postSignupMonitors,
        IReadOnlyDictionary<string, EnterMatchFlowSessionState> flowStates,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (options.MatchStartAckFactory is not null)
        {
            return await RunPhaseConcurrentOnLoopAsync(
                bundles,
                bundle => RunEnterMatchAsync(bundle, config, options, cancellationToken),
                static (timing, elapsed) => timing.EnterMatchDuration = elapsed);
        }

        PostSignupDiagnosticMonitor.SeedFlowStates(postSignupMonitors, flowStates);

        var phaseStopwatch = Stopwatch.StartNew();

        var startResults = await RunPhaseConcurrentOnLoopAsync(
            bundles,
            bundle => enterMatchFlow.WaitForMatchStartAsync(
                bundle.Session,
                config.Timeouts.MatchStartTimeout,
                bundle.Transport,
                flowStates[bundle.Session.Alias]),
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

        foreach (var bundle in bundles)
        {
            enterMatchFlow.ApplyMatchStartToSession(bundle.Session, matchStartByAlias[bundle.Session.Alias]);
        }

        var enterMatchResults = await RunPhaseConcurrentOnLoopAsync(
            bundles,
            bundle => enterMatchFlow.EnterMatchOnlyAsync(
                bundle.Session,
                matchStartByAlias[bundle.Session.Alias],
                config.Timeouts.EnterMatchTimeout,
                bundle.Transport),
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
                var matchStart = matchStartByAlias[bundle.Session.Alias];
                var seatOrder = await enterMatchFlow.EnterRoundOnlyAsync(
                    bundle.Session,
                    matchStart,
                    config.Timeouts.EnterRoundTimeout,
                    bundle.Transport,
                    flowStates[bundle.Session.Alias]);
                return enterMatchFlow.CreateSuccessResult(
                    bundle.Session,
                    matchStart,
                    seatOrder,
                    flowStates[bundle.Session.Alias]);
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

        ScenarioRunOptions options,

        CancellationToken cancellationToken)

    {

        _ = cancellationToken;



        if (options.MatchStartAckFactory is not null

            && bundle.Transport is FakeGameServerTransport fakeTransport)

        {

            var flowTask = enterMatchFlow.RunAsync(

                bundle.Session,

                config.Match,

                config.Timeouts.MatchStartTimeout,

                config.Timeouts.EnterMatchTimeout,

                config.Timeouts.EnterRoundTimeout,

                bundle.Transport);



            fakeTransport.DeliverIncomingMessage(options.MatchStartAckFactory(bundle.Session));



            return await flowTask;

        }



        return await enterMatchFlow.RunAsync(

            bundle.Session,

            config.Match,

            config.Timeouts.MatchStartTimeout,

            config.Timeouts.EnterMatchTimeout,

            config.Timeouts.EnterRoundTimeout,

            bundle.Transport);

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



        return new AccountBundle

        {

            Loop = loop,

            Session = session,

            Transport = transport,

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



    private static ScenarioFailureDetail? FindEnterMatchFailure(IReadOnlyList<PhaseResult<EnterMatchFlowResult>> results) =>

        FindFlowFailure(results, "Enter match failed.");



    private static ScenarioFailureDetail? FindMatchStartFailure(
        IReadOnlyList<PhaseResult<EnterMatchStartInfo>> results)
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
        IReadOnlyList<PhaseResult<bool>> results)
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


