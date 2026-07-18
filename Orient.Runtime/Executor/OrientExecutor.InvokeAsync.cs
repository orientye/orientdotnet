namespace Orient.Runtime;

public sealed partial class OrientExecutor
{
    public static OrientTask<T> InvokeAsync<T>(OrientExecutor targetExecutor, Func<OrientTask<T>> action)
    {
        ArgumentNullException.ThrowIfNull(targetExecutor);
        ArgumentNullException.ThrowIfNull(action);

        var callerExecutor = RequireCallerExecutor();
        var source = new OrientTaskCompletionSource<T>(callerExecutor);

        if (ReferenceEquals(callerExecutor, targetExecutor))
        {
            StartAsyncRunner(RunAsyncOnSameExecutor(action, source));
            return source.Task;
        }

        targetExecutor.Post(() => StartAsyncRunner(RunAsyncOnTargetExecutor(action, source, callerExecutor)));
        return source.Task;
    }

    public static OrientTask InvokeAsync(OrientExecutor targetExecutor, Func<OrientTask> action)
    {
        ArgumentNullException.ThrowIfNull(targetExecutor);
        ArgumentNullException.ThrowIfNull(action);

        var callerExecutor = RequireCallerExecutor();
        var source = new OrientTaskCompletionSource<OrientUnit>(callerExecutor);

        if (ReferenceEquals(callerExecutor, targetExecutor))
        {
            StartAsyncRunner(RunAsyncVoidOnSameExecutor(action, source));
            return new OrientTask(source.Task);
        }

        targetExecutor.Post(() => StartAsyncRunner(RunAsyncVoidOnTargetExecutor(action, source, callerExecutor)));
        return new OrientTask(source.Task);
    }

    public static OrientTask<T> InvokeAsync<T>(OrientExecutor targetExecutor, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(targetExecutor);
        ArgumentNullException.ThrowIfNull(action);

        var callerExecutor = RequireCallerExecutor();
        var source = new OrientTaskCompletionSource<T>(callerExecutor);

        if (ReferenceEquals(callerExecutor, targetExecutor))
        {
            RunSyncOnSameExecutor(action, source);
            return source.Task;
        }

        targetExecutor.Post(() => RunSyncOnTargetExecutor(action, source, callerExecutor));
        return source.Task;
    }

    public static OrientTask InvokeAsync(OrientExecutor targetExecutor, Action action)
    {
        ArgumentNullException.ThrowIfNull(targetExecutor);
        ArgumentNullException.ThrowIfNull(action);

        var callerExecutor = RequireCallerExecutor();
        var source = new OrientTaskCompletionSource<OrientUnit>(callerExecutor);

        if (ReferenceEquals(callerExecutor, targetExecutor))
        {
            RunVoidOnSameExecutor(action, source);
            return new OrientTask(source.Task);
        }

        targetExecutor.Post(() => RunVoidOnTargetExecutor(action, source, callerExecutor));
        return new OrientTask(source.Task);
    }

    private static OrientExecutor RequireCallerExecutor()
    {
        var callerExecutor = Current
            ?? throw new InvalidOperationException(
                "A OrientExecutor must be provided explicitly or available via OrientExecutor.Current.");

        callerExecutor.EnsureInExecutorThread();
        return callerExecutor;
    }

    private static void RunSyncOnSameExecutor<T>(Func<T> action, OrientTaskCompletionSource<T> source)
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

    private static void RunSyncOnTargetExecutor<T>(
        Func<T> action,
        OrientTaskCompletionSource<T> source,
        OrientExecutor callerExecutor)
    {
        try
        {
            var result = action();
            callerExecutor.Post(() => source.TrySetResult(result));
        }
        catch (Exception exception)
        {
            callerExecutor.Post(() => source.TrySetException(exception));
        }
    }

    private static void RunVoidOnSameExecutor(Action action, OrientTaskCompletionSource<OrientUnit> source)
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

    private static void RunVoidOnTargetExecutor(
        Action action,
        OrientTaskCompletionSource<OrientUnit> source,
        OrientExecutor callerExecutor)
    {
        try
        {
            action();
            callerExecutor.Post(() => source.TrySetResult(OrientUnit.Value));
        }
        catch (Exception exception)
        {
            callerExecutor.Post(() => source.TrySetException(exception));
        }
    }

    private static void StartAsyncRunner(OrientTask runner)
    {
        // Runner continuations stay on the target executor; InvokeAsync returns before completion.
        _ = runner;
    }

    private static async OrientTask RunAsyncOnSameExecutor<T>(
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

    private static async OrientTask RunAsyncOnTargetExecutor<T>(
        Func<OrientTask<T>> action,
        OrientTaskCompletionSource<T> source,
        OrientExecutor callerExecutor)
    {
        try
        {
            var result = await action();
            callerExecutor.Post(() => source.TrySetResult(result));
        }
        catch (TaskCanceledException)
        {
            callerExecutor.Post(() => source.TrySetCanceled());
        }
        catch (Exception exception)
        {
            callerExecutor.Post(() => source.TrySetException(exception));
        }
    }

    private static async OrientTask RunAsyncVoidOnSameExecutor(
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

    private static async OrientTask RunAsyncVoidOnTargetExecutor(
        Func<OrientTask> action,
        OrientTaskCompletionSource<OrientUnit> source,
        OrientExecutor callerExecutor)
    {
        try
        {
            await action();
            callerExecutor.Post(() => source.TrySetResult(OrientUnit.Value));
        }
        catch (TaskCanceledException)
        {
            callerExecutor.Post(() => source.TrySetCanceled());
        }
        catch (Exception exception)
        {
            callerExecutor.Post(() => source.TrySetException(exception));
        }
    }
}
