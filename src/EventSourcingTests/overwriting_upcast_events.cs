using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class overwriting_upcast_events: OneOffConfigurationsContext
{
    public record PersonRegisteredV1(string Name);

    public record PersonRegistered(string FullName);

    [Fact]
    public async Task masking_an_upcast_event_reads_back_the_masked_value()
    {
        StoreOptions(opts =>
        {
            opts.Events.Upcast<PersonRegisteredV1, PersonRegistered>(x => new PersonRegistered(x.Name));
            opts.Events.AddMaskingRuleForProtectedInformation<PersonRegistered>(x => x with { FullName = "****" });
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new PersonRegisteredV1("Alice"));
            await session.SaveChangesAsync();
        }

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeStream(streamId));

        await using var query = theStore.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);
        events[0].Data.ShouldBeOfType<PersonRegistered>().FullName.ShouldBe("****");
    }
}
