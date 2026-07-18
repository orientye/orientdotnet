using System.Runtime.CompilerServices;

namespace Orient.Runtime;

public struct OrientAsyncMethodBuilder
{
    private OrientTaskCompletionSource<OrientUnit>? source;

    public static OrientAsyncMethodBuilder Create()
    {
        return new OrientAsyncMethodBuilder
        {
            source = new OrientTaskCompletionSource<OrientUnit>(OrientExecutor.RequireCurrentOr())
        };
    }

    public OrientTask Task => new(GetSource().Task);

    public void SetResult()
    {
        GetSource().TrySetResult(OrientUnit.Value);
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

    private OrientTaskCompletionSource<OrientUnit> GetSource()
    {
        source ??= new OrientTaskCompletionSource<OrientUnit>(OrientExecutor.RequireCurrentOr());
        return source;
    }
}
