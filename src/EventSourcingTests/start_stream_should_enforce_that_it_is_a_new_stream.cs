using System;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests;

public class start_stream_should_enforce_that_it_is_a_new_stream: OneOffConfigurationsContext
{
    [Fact]
    public void throw_exception_if_start_stream_is_called_on_existing_stream()
    {
        var stream = Guid.NewGuid();

        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined());
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined());
            Exception<ExistingStreamIdCollisionException>.ShouldBeThrownBy(() =>
            {
                session.SaveChanges();
            });
        }
    }

    [Fact]
    public void throw_exception_if_start_stream_is_called_on_existing_stream_with_the_same_tenant()
    {
        var stream = Guid.NewGuid();
        const string tenantName = "Tenant";

        using (var session = theStore.LightweightSession(tenantName))
        {
            session.Events.StartStream(stream, new MembersJoined());
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession("Tenant"))
        {
            session.Events.StartStream(stream, new MembersJoined());
            Exception<ExistingStreamIdCollisionException>.ShouldBeThrownBy(() =>
            {
                session.SaveChanges();
            });
        }
    }

    [Fact]
    public void does_not_throw_exception_if_start_stream_is_called_on_existing_stream_with_the_same_tenant_and_tenancy_style_conjoined()
    {
        StoreOptions(_ => _.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined);

        var stream = Guid.NewGuid();
        const string tenantName = "Tenant";

        using (var session = theStore.LightweightSession(tenantName))
        {
            session.Events.StartStream(stream, new MembersJoined());
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession(tenantName))
        {
            session.Events.StartStream(stream, new MembersJoined());
            Exception<ExistingStreamIdCollisionException>.ShouldBeThrownBy(() =>
            {
                session.SaveChanges();
            });
        }
    }

    [Fact]
    public void does_not_throw_exception_if_start_stream_is_called_on_existing_stream_with_different_tenant_and_tenancy_style_conjoined()
    {
        StoreOptions(_ => _.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined);

        var stream = Guid.NewGuid();

        using (var session = theStore.LightweightSession("Tenant"))
        {
            session.Events.StartStream(stream, new MembersJoined());
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession("OtherTenant"))
        {
            session.Events.StartStream(stream, new MembersJoined());
            session.SaveChanges();
        }
    }

}
