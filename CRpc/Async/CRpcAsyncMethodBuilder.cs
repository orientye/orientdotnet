using System.Runtime.CompilerServices;

namespace CRpc.Async;

public struct CRpcAsyncMethodBuilder
{
    private CRpcTaskCompletionSource<CRpcUnit>? source;

    public static CRpcAsyncMethodBuilder Create()
    {
        return new CRpcAsyncMethodBuilder
        {
            source = new CRpcTaskCompletionSource<CRpcUnit>(CRpcLoop.RequireCurrentOr())
        };
    }

    public CRpcTask Task => new(GetSource().Task);

    public void SetResult()
    {
        GetSource().TrySetResult(CRpcUnit.Value);
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

    private CRpcTaskCompletionSource<CRpcUnit> GetSource()
    {
        source ??= new CRpcTaskCompletionSource<CRpcUnit>(CRpcLoop.RequireCurrentOr());
        return source;
    }
}
