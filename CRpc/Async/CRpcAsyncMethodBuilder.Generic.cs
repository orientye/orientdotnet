using System.Runtime.CompilerServices;

namespace CRpc.Async;

public struct CRpcAsyncMethodBuilder<T>
{
    private CRpcTaskCompletionSource<T>? source;

    public static CRpcAsyncMethodBuilder<T> Create()
    {
        return new CRpcAsyncMethodBuilder<T>
        {
            source = new CRpcTaskCompletionSource<T>(CRpcLoop.RequireCurrentOr())
        };
    }

    public CRpcTask<T> Task => GetSource().Task;

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

    private CRpcTaskCompletionSource<T> GetSource()
    {
        source ??= new CRpcTaskCompletionSource<T>(CRpcLoop.RequireCurrentOr());
        return source;
    }
}
