using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_3221_assert_on_wrong_identity_type_from_multi_stream_projection_to_slicer : BugIntegrationContext
{
    [Fact]
    public void blow_up_with_helpful_exception()
    {
        var ex = Should.Throw<InvalidProjectionException>(() =>
        {
            StoreOptions(opts =>
            {
                opts.Policies.AllDocumentsAreMultiTenanted();
                opts.Events.TenancyStyle = TenancyStyle.Conjoined;

                opts.Projections.Add<MismatchedIdentityProjection>(ProjectionLifecycle.Async);
            });
        });

        ex.Message.ShouldContain("Id type mismatch. The projection identity type is string", Case.Insensitive);
    }
}

public class MismatchedIdentityProjection : MultiStreamProjection<Target, string>
{
    public MismatchedIdentityProjection()
    {
        Identity<IEvent<AEvent>>(c => c.TenantId);
    }

    public void Apply(Target state, IEvent<AEvent> e) => state.Number++;
}



