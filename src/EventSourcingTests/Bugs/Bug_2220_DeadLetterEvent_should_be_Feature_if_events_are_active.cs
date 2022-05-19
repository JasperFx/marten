using System.Linq;
using Marten;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs
{
    public class Bug_2220_DeadLetterEvent_should_be_Feature_if_events_are_active : BugIntegrationContext
    {
        [Fact]
        public void no_events_are_active()
        {
            theStore.Options.Storage.AllActiveFeatures(theStore.Tenancy.Default.Database)
                .Any(x => x.StorageType == typeof(DeadLetterEvent)).ShouldBeFalse();
        }

        [Fact]
        public void DeadLetterEvent_should_be_an_active_feature_if_events_are_active()
        {
            StoreOptions(opts =>
            {
                // This is enough for the events to be in place
                opts.Events.AddEventType(typeof(QuestEnded));
            });

            theStore.Options.Storage.AllActiveFeatures(theStore.Tenancy.Default.Database)
                .Any(x => x.StorageType == typeof(DeadLetterEvent)).ShouldBeTrue();
        }
    }
}
