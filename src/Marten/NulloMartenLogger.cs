using Microsoft.Extensions.Logging.Abstractions;

namespace Marten
{
    internal class NulloMartenLogger: DefaultMartenLogger
    {
        public NulloMartenLogger(): base(NullLogger.Instance)
        {
        }

        public static IMartenSessionLogger Flyweight { get; } = new NulloMartenLogger();
    }
}
