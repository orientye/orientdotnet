using System.Reflection;
using CRpc.Rpc.CRpc;
using DotNetty.Buffers;

namespace CRPC.Tests;

public class ChannelWriteUtilTests
{
    [Fact]
    public void WriteEncodedFrameReleasesFrameOnceWhenSubmitThrowsSynchronously()
    {
        var allocator = PooledByteBufferAllocator.Default;
        IByteBuffer? encodedFrame = null;

        var method = typeof(ChannelWriteUtil).GetMethod(
            "WriteEncodedFrame",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(IByteBufferAllocator), typeof(Func<IByteBuffer, Task>), typeof(int), typeof(Action<IByteBuffer>) },
            modifiers: null);
        Assert.NotNull(method);

        var exception = Assert.Throws<TargetInvocationException>(() =>
            method!.Invoke(
                null,
                new object?[]
                {
                    allocator,
                    (Func<IByteBuffer, Task>)(_ => throw new InvalidOperationException("sync write failed")),
                    4,
                    (Action<IByteBuffer>)(frame =>
                    {
                        encodedFrame = frame;
                        frame.WriteInt(1);
                    })
                }));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.NotNull(encodedFrame);
        Assert.Equal(0, encodedFrame!.ReferenceCount);
    }
}
