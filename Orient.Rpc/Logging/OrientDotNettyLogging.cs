using DotNetty.Common.Internal.Logging;
using Microsoft.Extensions.Logging;
using Orient.Logging;

namespace Orient.Rpc.Logging;

public static class OrientDotNettyLogging
{
    public static IDisposable Install(IOrientLoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var previous = InternalLoggerFactory.DefaultFactory;
        InternalLoggerFactory.DefaultFactory = new OrientInternalLoggerFactory(loggerFactory);
        return new RestoreFactory(previous);
    }

    private sealed class RestoreFactory(ILoggerFactory previous) : IDisposable
    {
        private ILoggerFactory? previousFactory = previous;

        public void Dispose()
        {
            var factory = Interlocked.Exchange(ref previousFactory, null);
            if (factory is not null)
            {
                InternalLoggerFactory.DefaultFactory = factory;
            }
        }
    }
}
