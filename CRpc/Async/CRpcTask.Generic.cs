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

        /// <summary>
        /// True when the task completed and the current thread is the task's owner <see cref="CRpcLoop"/> thread.
        /// On other threads this is always false, even if the task has already completed.
        /// </summary>
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
