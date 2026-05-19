using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3184_Projection_named_Projection : BugIntegrationContext
{
    [Fact]
    public async Task codegen_works()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<Projection>(ProjectionLifecycle.Inline);
        });

        theSession.Events.Append(Guid.NewGuid(), new AccountCreated());
        await theSession.SaveChangesAsync();
    }
}

public class AccountCreated
{

}

public record AccountId(Guid Id);

public class AccountListInformation
{
    public Guid Id { get; set; }
    public AccountId AccountId { get; private set; }
    public string Name { get; private set; }
    public decimal Balance { get; private set; }

    internal void ApplyAccountCreated(AccountCreated _)
    {
        // Id = accountCreated.Id.Value;
        // AccountId = accountCreated.Id;
        // Name = accountCreated.Name;
        // Balance = 0;
    }
}

// Regression guard for #3184: a projection class literally named "Projection"
// must not collide with any Marten-internal `Projection` type or with the
// source generator's [GeneratedEvolver] partial-class emission. The pre-9.0
// bug originated in the now-retired Roslyn codegen pipeline; the present-day
// equivalent risk is the JasperFx.Events.SourceGenerator emitting a
// `partial class Projection` in the same namespace. Un-nested from
// AccountListInformation in 9.0 because the source generator does not yet
// emit `[GeneratedEvolver]` partials inside nested classes; the aggregate's
// own would-be `Apply(AccountCreated)` was renamed so the source generator
// doesn't see two competing handlers for the same event type.
public partial class Projection : SingleStreamProjection<AccountListInformation, Guid>
{
    public void Apply(AccountCreated e, AccountListInformation i) => i.ApplyAccountCreated(e);
}
