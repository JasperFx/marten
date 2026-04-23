using System;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class start_stream_should_enforce_that_it_is_a_new_stream_when_UseArchivedStreamPartitioning_is_enabled: OneOffConfigurationsContext
{
    [Fact]
    public async Task throw_exception_if_start_stream_is_called_on_existing_stream_when_UseArchivedStreamPartitioning()
    {
        StoreOptions(_ => _.Events.UseArchivedStreamPartitioning = true);

        var stream = Guid.NewGuid();

        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined());

            await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () =>
            {
                await session.SaveChangesAsync();
            });
        }
    }
}
