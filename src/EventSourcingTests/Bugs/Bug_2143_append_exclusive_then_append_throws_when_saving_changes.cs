using Marten.Testing.Harness;
using Shouldly;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

public class Bug_2143_append_exclusive_then_append_throws_when_saving_changes : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2143_append_exclusive_then_append_throws_when_saving_changes(ITestOutputHelper output)
    {
        _output = output;

        TheSession.Logger = new TestOutputMartenLogger(output);
    }

    [Fact]
    public async Task stream_can_be_saved_when_combining_append_exclusive_and_append()
    {
        var streamId = Guid.NewGuid();

        TheSession.Events.Append(streamId, new QuestStarted { Name = "One" });
        await TheSession.SaveChangesAsync();

        await Task.Delay(25);

        await TheSession.Events.AppendExclusive(streamId);
        TheSession.Events.Append(streamId, new QuestStarted { Name = "Two" }, new QuestStarted { Name = "Three" });
        await TheSession.SaveChangesAsync();

        var streamState = await TheSession.Events.FetchStreamStateAsync(streamId);

        streamState.Version.ShouldBe(3);
    }
}