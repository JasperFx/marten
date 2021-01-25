using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing
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

            await theTracker.Complete();

            // await theTracker.WaitForShardState("foo", 35, 30.Seconds());
            // await theTracker.WaitForShardState("bar", 45, 30.Seconds());
            // await theTracker.WaitForShardState("baz", 55, 30.Seconds());

            observer1.States.ShouldHaveTheSameElementsAs(state1, state2, state3);
            observer2.States.ShouldHaveTheSameElementsAs(state1, state2, state3);
            observer3.States.ShouldHaveTheSameElementsAs(state1, state2, state3);


            theTracker.Finish();

        }

        [Fact]
        public void default_state_action_is_update()
        {
            new ShardState("foo", 22L)
                .Action.ShouldBe(ShardAction.Update);
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
