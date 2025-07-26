using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3880_able_to_use_different_event_types_with_same_name : BugIntegrationContext
{
    [Theory]
    [InlineData(EventNamingStyle.SmarterTypeName)]
    [InlineData(EventNamingStyle.FullTypeName)]
    public async Task append_and_load(EventNamingStyle namingStyle)
    {
        StoreOptions(opts =>
        {
            opts.Events.EventNamingStyle = namingStyle;
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new GroupEvents.Created("foo"), new UserEvents.Created("bar"));
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);

        events[0].ShouldBeOfType<Event<GroupEvents.Created>>().Data.Name.ShouldBe("foo");
        events[1].ShouldBeOfType<Event<UserEvents.Created>>().Data.Name.ShouldBe("bar");
    }
}

public class GroupEvents
{
    public record Created(string Name);
}

public class UserEvents
{
    public record Created(string Name);
}
