using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    internal class EventWaiter
    {
        public readonly TaskCompletionSource<long> Completion = new TaskCompletionSource<long>();

        public EventWaiter(long sequence)
        {
            Sequence = sequence;
        }

        public long Sequence { get; set; }
    }
}