using System.Runtime.CompilerServices;

namespace CRpc.Async;

[AsyncMethodBuilder(typeof(CRpcAsyncMethodBuilder<>))]
public readonly struct CRpcTask<T>
{
    private readonly CRpcTaskCompletionSource<T>? source;

    internal CRpcTask(CRpcTaskCompletionSource<T> source)
    {
        this.source = source;
    }

    public Awaiter GetAwaiter()
    {
        return new Awaiter(source);
    }

    public readonly struct Awaiter : INotifyCompletion
    {
        private readonly CRpcTaskCompletionSource<T>? source;

        internal Awaiter(CRpcTaskCompletionSource<T>? source)
        {
            this.source = source;
        }

        public bool IsCompleted => source is not null && source.IsCompletedOnCurrentThread;

        public void OnCompleted(Action continuation)
        {
            if (source is null)
            {
                throw new InvalidOperationException("CRpcTask has no source.");
            }

            source.OnCompleted(continuation);
        }

        public T GetResult()
        {
            if (source is null)
            {
                throw new InvalidOperationException("CRpcTask has no source.");
            }

            return source.GetResult();
        }
    }
}
