using System.Runtime.CompilerServices;

namespace Orient.Runtime;

public struct OrientAsyncMethodBuilder<T>
{
    private OrientTaskCompletionSource<T>? source;

    public static OrientAsyncMethodBuilder<T> Create()
    {
        return new OrientAsyncMethodBuilder<T>
        {
            source = new OrientTaskCompletionSource<T>(OrientLoop.RequireCurrentOr())
        };
    }

    public OrientTask<T> Task => GetSource().Task;

    public void SetResult(T result)
    {
        GetSource().TrySetResult(result);
    }

    public void SetException(Exception exception)
    {
        GetSource().TrySetException(exception);
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
    }

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        stateMachine.MoveNext();
    }

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        IAsyncStateMachine boxedStateMachine = stateMachine;
        awaiter.OnCompleted(boxedStateMachine.MoveNext);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        IAsyncStateMachine boxedStateMachine = stateMachine;
        awaiter.UnsafeOnCompleted(boxedStateMachine.MoveNext);
    }

    private OrientTaskCompletionSource<T> GetSource()
    {
        source ??= new OrientTaskCompletionSource<T>(OrientLoop.RequireCurrentOr());
        return source;
    }
}
