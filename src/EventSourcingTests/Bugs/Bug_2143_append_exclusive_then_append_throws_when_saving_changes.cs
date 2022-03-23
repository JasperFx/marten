using Marten.Testing.Harness;
using Shouldly;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingTests.Bugs
{
    public class Bug_2143_append_exclusive_then_append_throws_when_saving_changes : BugIntegrationContext
    {
        [Fact]
        public async Task stream_can_be_saved_when_combining_append_exclusive_and_append()
        {
            var streamId = Guid.NewGuid();

            theSession.Events.Append(streamId, new QuestStarted { Name = "One" });
            await theSession.SaveChangesAsync();

            await Task.Delay(25);

            await theSession.Events.AppendExclusive(streamId);
            theSession.Events.Append(streamId, new QuestStarted { Name = "Two" }, new QuestStarted { Name = "Three" });
            await theSession.SaveChangesAsync();

            var streamState = await theSession.Events.FetchStreamStateAsync(streamId);

            streamState.Version.ShouldBe(3);
        }
    }
}
