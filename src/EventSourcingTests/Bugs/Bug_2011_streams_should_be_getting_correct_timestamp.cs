using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs
{
    public class Bug_2011_streams_should_be_getting_correct_timestamp : BugIntegrationContext
    {
        [Fact]
        public async Task capture_multiple_streams_and_check_created()
        {
            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();
            var stream3 = Guid.NewGuid();
            var stream4 = Guid.NewGuid();

            theSession.Events.Append(stream1, new QuestStarted { Name = "One" });
            await theSession.SaveChangesAsync();

            await Task.Delay(25);

            theSession.Events.Append(stream2, new QuestStarted { Name = "Two" });
            await theSession.SaveChangesAsync();

            await Task.Delay(500);

            theSession.Events.Append(stream3, new QuestStarted { Name = "Three" });
            await theSession.SaveChangesAsync();

            await Task.Delay(500);

            theSession.Events.Append(stream4, new QuestStarted { Name = "Four" });
            await theSession.SaveChangesAsync();

            var s1 = await theSession.Events.FetchStreamStateAsync(stream1);
            var s2 = await theSession.Events.FetchStreamStateAsync(stream2);
            var s3 = await theSession.Events.FetchStreamStateAsync(stream3);
            var s4 = await theSession.Events.FetchStreamStateAsync(stream4);

            var dates = new DateTimeOffset[] { s1.Created, s2.Created, s3.Created, s4.Created };
            dates.Distinct().Count().ShouldBe(4);
        }


    }
}
