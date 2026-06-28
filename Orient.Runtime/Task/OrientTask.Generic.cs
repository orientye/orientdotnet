using System.Runtime.CompilerServices;

namespace Orient.Runtime;

[AsyncMethodBuilder(typeof(OrientAsyncMethodBuilder<>))]
public readonly struct OrientTask<T>
{
    private readonly OrientTaskCompletionSource<T>? source;

    internal OrientTask(OrientTaskCompletionSource<T> source)
    {
        this.source = source;
    }

    public Awaiter GetAwaiter()
    {
        return new Awaiter(source);
    }

    public readonly struct Awaiter : INotifyCompletion
    {
        private readonly OrientTaskCompletionSource<T>? source;

        internal Awaiter(OrientTaskCompletionSource<T>? source)
        {
            this.source = source;
        }

        /// <summary>
        /// True when the task completed and the current thread is the task's owner <see cref="OrientLoop"/> thread.
        /// On other threads this is always false, even if the task has already completed.
        /// </summary>
        public bool IsCompleted => source is not null && source.IsCompletedOnCurrentThread;

        public void OnCompleted(Action continuation)
        {
            if (source is null)
            {
                throw new InvalidOperationException("OrientTask has no source.");
            }

            source.OnCompleted(continuation);
        }

        public T GetResult()
        {
            if (source is null)
            {
                throw new InvalidOperationException("OrientTask has no source.");
            }

            return source.GetResult();
        }
    }
}
