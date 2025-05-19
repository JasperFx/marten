using System;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class start_stream_should_enforce_that_it_is_a_new_stream: OneOffConfigurationsContext
{
    [Fact]
    public async Task throw_exception_if_start_stream_is_called_on_existing_stream()
    {
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

    [Fact]
    public async Task throw_exception_if_start_stream_is_called_on_existing_stream_with_the_same_tenant()
    {
        var stream = Guid.NewGuid();
        const string tenantName = "Tenant";

        using (var session = theStore.LightweightSession(tenantName))
        {
            session.Events.StartStream(stream, new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("Tenant"))
        {
            session.Events.StartStream(stream, new MembersJoined());

            await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () =>
            {
                await session.SaveChangesAsync();
            });
        }
    }

    [Fact]
    public async Task does_not_throw_exception_if_start_stream_is_called_on_existing_stream_with_the_same_tenant_and_tenancy_style_conjoined()
    {
        StoreOptions(_ => _.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined);

        var stream = Guid.NewGuid();
        const string tenantName = "Tenant";

        using (var session = theStore.LightweightSession(tenantName))
        {
            session.Events.StartStream(stream, new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession(tenantName))
        {
            session.Events.StartStream(stream, new MembersJoined());

            await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () =>
            {
                await session.SaveChangesAsync();
            });
        }
    }

    [Fact]
    public async Task does_not_throw_exception_if_start_stream_is_called_on_existing_stream_with_different_tenant_and_tenancy_style_conjoined()
    {
        StoreOptions(_ => _.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined);

        var stream = Guid.NewGuid();

        using (var session = theStore.LightweightSession("Tenant"))
        {
            session.Events.StartStream(stream, new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("OtherTenant"))
        {
            session.Events.StartStream(stream, new MembersJoined());
            await session.SaveChangesAsync();
        }
    }

}
