using System.Diagnostics;

namespace Orient.Runtime;

internal sealed class MinHeapTimerScheduler : IOrientExecutorTimerScheduler
{
    private readonly List<HeapEntry> heap = new();

    internal int TimerCount => heap.Count;

    public OrientExecutorTimer ScheduleAt(long dueTimestamp, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new OrientExecutorTimer(callback);
        timer.BindToScheduler(() => RemoveTimer(timer));

        var index = heap.Count;
        timer.HeapIndex = index;
        heap.Add(new HeapEntry(timer, dueTimestamp));
        SiftUp(index);
        return timer;
    }

    public int RunDueTimers(long now, int maxTimers)
    {
        var ran = 0;
        while (ran < maxTimers
               && heap.Count > 0
               && heap[0].DueTimestamp <= now)
        {
            var entry = PopMin();
            ran++;
            entry.Timer.Invoke();
        }

        return ran;
    }

    public TimeSpan? GetDelayUntilNextWakeup(long now)
    {
        if (heap.Count == 0)
        {
            return null;
        }

        var dueTimestamp = heap[0].DueTimestamp;
        if (dueTimestamp <= now)
        {
            return TimeSpan.Zero;
        }

        var ticks = dueTimestamp - now;
        return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
    }

    private HeapEntry PopMin()
    {
        var entry = heap[0];
        RemoveAtIndex(0);
        return entry;
    }

    private void RemoveTimer(OrientExecutorTimer timer)
    {
        var index = timer.HeapIndex;
        if (index < 0)
        {
            return;
        }

        RemoveAtIndex(index);
    }

    private void RemoveAtIndex(int index)
    {
        var timer = heap[index].Timer;
        var lastIndex = heap.Count - 1;
        if (index != lastIndex)
        {
            heap[index] = heap[lastIndex];
            heap[index].Timer.HeapIndex = index;
        }

        heap.RemoveAt(lastIndex);
        timer.HeapIndex = -1;
        timer.UnbindFromScheduler();

        if (index < heap.Count)
        {
            var parent = (index - 1) / 2;
            if (index > 0 && heap[index].DueTimestamp < heap[parent].DueTimestamp)
            {
                SiftUp(index);
            }
            else
            {
                SiftDown(index);
            }
        }
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            var parent = (index - 1) / 2;
            if (heap[index].DueTimestamp >= heap[parent].DueTimestamp)
            {
                break;
            }

            Swap(index, parent);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        while (true)
        {
            var left = index * 2 + 1;
            var right = left + 1;
            var smallest = index;

            if (left < heap.Count && heap[left].DueTimestamp < heap[smallest].DueTimestamp)
            {
                smallest = left;
            }

            if (right < heap.Count && heap[right].DueTimestamp < heap[smallest].DueTimestamp)
            {
                smallest = right;
            }

            if (smallest == index)
            {
                break;
            }

            Swap(index, smallest);
            index = smallest;
        }
    }

    private void Swap(int a, int b)
    {
        (heap[a], heap[b]) = (heap[b], heap[a]);
        heap[a].Timer.HeapIndex = a;
        heap[b].Timer.HeapIndex = b;
    }

    private sealed class HeapEntry
    {
        public HeapEntry(OrientExecutorTimer timer, long dueTimestamp)
        {
            Timer = timer;
            DueTimestamp = dueTimestamp;
        }

        public OrientExecutorTimer Timer { get; }

        public long DueTimestamp { get; }
    }
}
