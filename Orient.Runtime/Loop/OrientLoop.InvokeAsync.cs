namespace Orient.Runtime;

public sealed partial class OrientLoop
{
    public static OrientTask<T> InvokeAsync<T>(OrientLoop targetLoop, Func<OrientTask<T>> action)
    {
        ArgumentNullException.ThrowIfNull(targetLoop);
        ArgumentNullException.ThrowIfNull(action);

        var callerLoop = RequireCallerLoop();
        var source = new OrientTaskCompletionSource<T>(callerLoop);

        if (ReferenceEquals(callerLoop, targetLoop))
        {
            StartAsyncRunner(RunAsyncOnSameLoop(action, source));
            return source.Task;
        }

        targetLoop.Post(() => StartAsyncRunner(RunAsyncOnTargetLoop(action, source, callerLoop)));
        return source.Task;
    }

    public static OrientTask InvokeAsync(OrientLoop targetLoop, Func<OrientTask> action)
    {
        ArgumentNullException.ThrowIfNull(targetLoop);
        ArgumentNullException.ThrowIfNull(action);

        var callerLoop = RequireCallerLoop();
        var source = new OrientTaskCompletionSource<OrientUnit>(callerLoop);

        if (ReferenceEquals(callerLoop, targetLoop))
        {
            StartAsyncRunner(RunAsyncVoidOnSameLoop(action, source));
            return new OrientTask(source.Task);
        }

        targetLoop.Post(() => StartAsyncRunner(RunAsyncVoidOnTargetLoop(action, source, callerLoop)));
        return new OrientTask(source.Task);
    }

    public static OrientTask<T> InvokeAsync<T>(OrientLoop targetLoop, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(targetLoop);
        ArgumentNullException.ThrowIfNull(action);

        var callerLoop = RequireCallerLoop();
        var source = new OrientTaskCompletionSource<T>(callerLoop);

        if (ReferenceEquals(callerLoop, targetLoop))
        {
            RunSyncOnSameLoop(action, source);
            return source.Task;
        }

        targetLoop.Post(() => RunSyncOnTargetLoop(action, source, callerLoop));
        return source.Task;
    }

    public static OrientTask InvokeAsync(OrientLoop targetLoop, Action action)
    {
        ArgumentNullException.ThrowIfNull(targetLoop);
        ArgumentNullException.ThrowIfNull(action);

        var callerLoop = RequireCallerLoop();
        var source = new OrientTaskCompletionSource<OrientUnit>(callerLoop);

        if (ReferenceEquals(callerLoop, targetLoop))
        {
            RunVoidOnSameLoop(action, source);
            return new OrientTask(source.Task);
        }

        targetLoop.Post(() => RunVoidOnTargetLoop(action, source, callerLoop));
        return new OrientTask(source.Task);
    }

    private static OrientLoop RequireCallerLoop()
    {
        var callerLoop = Current
            ?? throw new InvalidOperationException(
                "A OrientLoop must be provided explicitly or available via OrientLoop.Current.");

        callerLoop.EnsureInLoopThread();
        return callerLoop;
    }

    private static void RunSyncOnSameLoop<T>(Func<T> action, OrientTaskCompletionSource<T> source)
    {
        try
        {
            source.TrySetResult(action());
        }
        catch (Exception exception)
        {
            source.TrySetException(exception);
        }
    }

    private static void RunSyncOnTargetLoop<T>(
        Func<T> action,
        OrientTaskCompletionSource<T> source,
        OrientLoop callerLoop)
    {
        try
        {
            var result = action();
            callerLoop.Post(() => source.TrySetResult(result));
        }
        catch (Exception exception)
        {
            callerLoop.Post(() => source.TrySetException(exception));
        }
    }

    private static void RunVoidOnSameLoop(Action action, OrientTaskCompletionSource<OrientUnit> source)
    {
        try
        {
            action();
            source.TrySetResult(OrientUnit.Value);
        }
        catch (Exception exception)
        {
            source.TrySetException(exception);
        }
    }

    private static void RunVoidOnTargetLoop(
        Action action,
        OrientTaskCompletionSource<OrientUnit> source,
        OrientLoop callerLoop)
    {
        try
        {
            action();
            callerLoop.Post(() => source.TrySetResult(OrientUnit.Value));
        }
        catch (Exception exception)
        {
            callerLoop.Post(() => source.TrySetException(exception));
        }
    }

    private static void StartAsyncRunner(OrientTask runner)
    {
        // Runner continuations stay on the target loop; InvokeAsync returns before completion.
        _ = runner;
    }

    private static async OrientTask RunAsyncOnSameLoop<T>(
        Func<OrientTask<T>> action,
        OrientTaskCompletionSource<T> source)
    {
        try
        {
            source.TrySetResult(await action());
        }
        catch (TaskCanceledException)
        {
            source.TrySetCanceled();
        }
        catch (Exception exception)
        {
            source.TrySetException(exception);
        }
    }

    private static async OrientTask RunAsyncOnTargetLoop<T>(
        Func<OrientTask<T>> action,
        OrientTaskCompletionSource<T> source,
        OrientLoop callerLoop)
    {
        try
        {
            var result = await action();
            callerLoop.Post(() => source.TrySetResult(result));
        }
        catch (TaskCanceledException)
        {
            callerLoop.Post(() => source.TrySetCanceled());
        }
        catch (Exception exception)
        {
            callerLoop.Post(() => source.TrySetException(exception));
        }
    }

    private static async OrientTask RunAsyncVoidOnSameLoop(
        Func<OrientTask> action,
        OrientTaskCompletionSource<OrientUnit> source)
    {
        try
        {
            await action();
            source.TrySetResult(OrientUnit.Value);
        }
        catch (TaskCanceledException)
        {
            source.TrySetCanceled();
        }
        catch (Exception exception)
        {
            source.TrySetException(exception);
        }
    }

    private static async OrientTask RunAsyncVoidOnTargetLoop(
        Func<OrientTask> action,
        OrientTaskCompletionSource<OrientUnit> source,
        OrientLoop callerLoop)
    {
        try
        {
            await action();
            callerLoop.Post(() => source.TrySetResult(OrientUnit.Value));
        }
        catch (TaskCanceledException)
        {
            callerLoop.Post(() => source.TrySetCanceled());
        }
        catch (Exception exception)
        {
            callerLoop.Post(() => source.TrySetException(exception));
        }
    }
}
