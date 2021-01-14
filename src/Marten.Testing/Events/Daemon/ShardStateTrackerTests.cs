using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Events.Daemon
{
    public class ShardStateTrackerTests : IDisposable
    {
        private ShardStateTracker theTracker = new ShardStateTracker();

        public void Dispose()
        {
            theTracker.Dispose();
        }

        [Fact]
        public async Task calls_back_to_observer()
        {
            var observer1 = new Observer();
            var observer2 = new Observer();
            var observer3 = new Observer();

            var state1 = new ShardState("foo", 35);
            var state2 = new ShardState("bar", 45);
            var state3 = new ShardState("baz", 55);

            theTracker.Subscribe(observer1);
            theTracker.Subscribe(observer2);
            theTracker.Subscribe(observer3);

            theTracker.Publish(state1);
            theTracker.Publish(state2);
            theTracker.Publish(state3);

            theTracker.Finish();

            observer1.States.ShouldHaveTheSameElementsAs(state1, state2, state3);
            observer2.States.ShouldHaveTheSameElementsAs(state1, state2, state3);
            observer3.States.ShouldHaveTheSameElementsAs(state1, state2, state3);


        }

        public class Observer: IObserver<ShardState>
        {
            public readonly IList<ShardState> States = new List<ShardState>();

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnNext(ShardState value)
            {
                States.Add(value);
            }
        }
    }
}
