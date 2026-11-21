#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

#region sample_tag_grouped_projection_events

// The tag type: a strong-typed identifier wrapping one value
public record BillingAccountId(Guid Value);

public record ChargePosted(Guid Account, decimal Amount);

public record PaymentReceived(Guid Account, decimal Amount);

// Names no account at all, so it gets its tags by hand
public record FeeCharged(decimal Amount);

#endregion

public class AccountBalance
{
    public Guid Id { get; set; }
    public decimal Balance { get; set; }
}

#region sample_tag_grouped_projection

public class AccountBalanceProjection: MultiStreamProjection<AccountBalance, Guid>
{
    public AccountBalanceProjection()
    {
        // Every event carrying a BillingAccountId tag goes to the account the tag names
        IdentityFromTag<BillingAccountId>();
    }

    public void Apply(ChargePosted e, AccountBalance balance) => balance.Balance -= e.Amount;

    public void Apply(PaymentReceived e, AccountBalance balance) => balance.Balance += e.Amount;

    // Applied once to every account the event is tagged with
    public void Apply(FeeCharged e, AccountBalance balance) => balance.Balance -= e.Amount;
}

#endregion

#region sample_delegate_grouped_projection

public class AccountBalanceByDelegateProjection: MultiStreamProjection<AccountBalance, Guid>
{
    public AccountBalanceByDelegateProjection()
    {
        // Works, but Marten can't run this backwards to find one account's events
        Identity<ChargePosted>(e => e.Account);
        Identity<PaymentReceived>(e => e.Account);
    }

    public void Apply(ChargePosted e, AccountBalance balance) => balance.Balance -= e.Amount;

    public void Apply(PaymentReceived e, AccountBalance balance) => balance.Balance += e.Amount;
}

#endregion

/// <summary>
///     marten#6117: the executable source of the samples in <c>docs/events/projections/grouping-by-tags.md</c>.
/// </summary>
public class tag_grouped_projection_samples: OneOffConfigurationsContext
{
    private static readonly TimeSpan Generous = 60.Seconds();

    private static void configureAccounts(StoreOptions opts)
    {
        #region sample_registering_a_tag_grouped_projection

        // One tag table, mt_event_tag_billing_account
        opts.Events.RegisterTagType<BillingAccountId>("billing_account");

        // Tag every charge and payment with its account when the event is built
        opts.Events.TagWith<ChargePosted>(e => new BillingAccountId(e.Account));
        opts.Events.TagWith<PaymentReceived>(e => new BillingAccountId(e.Account));

        opts.Projections.Add<AccountBalanceProjection>(ProjectionLifecycle.Async);

        #endregion
    }

    private async Task<decimal?> balanceAsync(Guid account, string? tenantId = null)
    {
        await using var query = tenantId == null ? theStore.QuerySession() : theStore.QuerySession(tenantId);
        return (await query.LoadAsync<AccountBalance>(account, TestContext.Current.CancellationToken))?.Balance;
    }

    [Fact]
    public async Task accounts_are_built_from_their_tagged_events()
    {
        StoreOptions(configureAccounts);

        var checking = Guid.NewGuid();
        var savings = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            // Tagged by the TagWith rules
            session.Events.StartStream(Guid.NewGuid(), new PaymentReceived(checking, 100m));
            session.Events.StartStream(Guid.NewGuid(), new ChargePosted(checking, 30m));

            #region sample_tagging_one_event_by_hand

            var fee = session.Events.BuildEvent(new FeeCharged(5m));
            fee.WithTag(new BillingAccountId(checking), new BillingAccountId(savings));
            session.Events.StartStream(Guid.NewGuid(), fee);

            #endregion

            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await theStore.WaitForNonStaleProjectionDataAsync(Generous);

        (await balanceAsync(checking)).ShouldBe(65m);
        (await balanceAsync(savings)).ShouldBe(-5m);

        #region sample_rebuilding_one_tag_grouped_identity

        // Re-folds one account from its tagged events, in the shard that runs the projection
        await daemon.RebuildIdentityAsync("AccountBalance", tenantId: null, checking,
            TestContext.Current.CancellationToken);

        #endregion

        (await balanceAsync(checking)).ShouldBe(65m);

        // The stepper and per-identity reads go straight to the tag table, and the page is exact
        var page = await ((IEventStore<IDocumentOperations, IQuerySession>)theStore).LoadIdentityEventsAsync((IEventDatabase)theStore.Storage.Database,
            new IdentityEventsRequest { ProjectionName = "AccountBalance", Identity = savings },
            TestContext.Current.CancellationToken);
        page.Source.ShouldBe(IdentityEventSource.Tag);
        page.Events.Single().Data.ShouldBeOfType<FeeCharged>();
        page.Exact.ShouldBeTrue();

        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task an_explicit_tag_puts_the_event_in_that_identity_whatever_the_body_says()
    {
        StoreOptions(configureAccounts);

        var checking = Guid.NewGuid();
        var other = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            // The rule tags this charge with checking, and the hand-added tag puts it in other too
            var charge = session.Events.BuildEvent(new ChargePosted(checking, 10m));
            charge.WithTag(new BillingAccountId(other));
            session.Events.StartStream(Guid.NewGuid(), charge);

            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await theStore.WaitForNonStaleProjectionDataAsync(Generous);

        (await balanceAsync(checking)).ShouldBe(-10m);
        (await balanceAsync(other)).ShouldBe(-10m);

        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task describe_shows_how_each_projection_finds_one_identity()
    {
        StoreOptions(opts =>
        {
            configureAccounts(opts);
            opts.Projections.Add(new AccountBalanceByDelegateProjection { Name = "AccountBalanceByDelegate" },
                ProjectionLifecycle.Async);
        });

        var usage = await ((IEventStore)theStore).TryCreateUsage(TestContext.Current.CancellationToken);
        usage.ShouldNotBeNull();

        var tagged = usage.Subscriptions.Single(x => x.Name == "AccountBalance");
        tagged.IdentityAccess.ShouldBe(IdentityAccessClass.Tag);

        var delegated = usage.Subscriptions.Single(x => x.Name == "AccountBalanceByDelegate");
        delegated.IdentityAccess.ShouldBe(IdentityAccessClass.ScanOnly);
        delegated.IdentityAccessReason.ShouldNotBeNull().ShouldContain("can't be inverted");
    }

    [Fact]
    public async Task each_tenant_gets_its_own_document_for_one_tag_value()
    {
        StoreOptions(opts =>
        {
            #region sample_tag_grouped_projection_with_conjoined_tenancy

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Schema.For<AccountBalance>().MultiTenanted();

            #endregion

            configureAccounts(opts);
        });

        var account = Guid.NewGuid();

        foreach (var (tenant, amount) in new[] { ("red", 10m), ("blue", 25m) })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.StartStream(Guid.NewGuid(), new PaymentReceived(account, amount));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await theStore.WaitForNonStaleProjectionDataAsync(Generous);

        (await balanceAsync(account, "red")).ShouldBe(10m);
        (await balanceAsync(account, "blue")).ShouldBe(25m);

        // One identity is rebuilt within its own tenant
        await daemon.RebuildIdentityAsync("AccountBalance", "blue", account, TestContext.Current.CancellationToken);
        (await balanceAsync(account, "red")).ShouldBe(10m);
        (await balanceAsync(account, "blue")).ShouldBe(25m);

        await daemon.StopAllAsync();
    }
}
