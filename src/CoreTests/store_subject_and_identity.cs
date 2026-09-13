using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
///     #5409 — the two surfaces that identify a store to anything outside it:
///     <c>IEventStore.Subject</c> and <c>IEventStore.Identity</c>, and the
///     <c>StoreOptions.StoreName</c> both are built from.
/// </summary>
/// <remarks>
///     <para>
///         <b>They disagreed.</b> <c>Identity</c> has always been built from <c>StoreName</c>, while
///         <c>Subject</c> was a property initializer holding the literal <c>marten://main</c> that only
///         <c>SecondaryStoreConfig.Build</c> ever overwrote. So naming a PRIMARY store moved one and
///         left the other behind, and <c>Subject</c> is the one consumers key on: CritterWatch resolves
///         every explorer read through <c>store.Subject.ToString()</c> and builds its shard progression
///         id from <c>(serviceName, storeUri, databaseIdentifier, tenantId, shardName)</c>.
///     </para>
///     <para>
///         The second half is the ancillary store's name being assigned twice — before and after the
///         <c>IConfigureMarten&lt;T&gt;</c> chain — so a contribution that named the store was silently
///         reverted. Polecat and Fisher both order that assignment so the override wins; Marten was the
///         only one of the three that took the name back.
///     </para>
/// </remarks>
public class store_subject_and_identity
{
    private static DocumentStore StoreFor(string? storeName, string schemaName)
        => DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schemaName;
            if (storeName is not null) opts.StoreName = storeName;
        });

    /// <summary>
    ///     The headline: a named primary store's subject follows its name, so it agrees with the
    ///     identity built from that same name rather than contradicting it.
    /// </summary>
    [Fact]
    public void a_named_primary_store_gets_a_subject_that_matches_its_identity()
    {
        using var store = StoreFor("Ledgers", "subject_named");

        var eventStore = (IEventStore)store;

        eventStore.Subject.ShouldBe(new Uri("marten://ledgers"));
        eventStore.Identity.Name.ShouldBe("ledgers");
        eventStore.Identity.Type.ShouldBe("marten");
    }

    /// <summary>
    ///     Two named stores are distinguishable, which is the whole point of the property.
    /// </summary>
    [Fact]
    public void two_named_stores_do_not_share_a_subject()
    {
        using var ledgers = StoreFor("Ledgers", "subject_a");
        using var archive = StoreFor("Archive", "subject_b");

        ((IEventStore)ledgers).Subject.ShouldNotBe(((IEventStore)archive).Subject);
    }

    /// <summary>
    ///     Regression guard — an unnamed store keeps <c>marten://main</c>, which several existing tests
    ///     and every existing consumer already depend on.
    /// </summary>
    [Fact]
    public void an_unnamed_primary_store_is_still_main()
    {
        using var store = StoreFor(null, "subject_default");

        ((IEventStore)store).Subject.ShouldBe(new Uri("marten://main"));
    }

    /// <summary>
    ///     A store name is user-supplied text and a uri host is not. Verified against .NET 10 before
    ///     this was written: <c>new Uri("marten://my store")</c> throws <c>UriFormatException</c>, and
    ///     <c>new Uri("marten://a/b")</c> silently parses the tail as a PATH — so a naive concatenation
    ///     would turn naming a store into a crash at construction.
    /// </summary>
    [Theory]
    [InlineData("My Store", "marten://my-store")]
    [InlineData("Orders/Archive", "marten://orders-archive")]
    [InlineData("Orders_v2", "marten://orders_v2")]
    public void a_store_name_a_uri_host_cannot_carry_is_folded_rather_than_thrown_on(string name, string expected)
    {
        using var store = StoreFor(name, "subject_exotic");

        ((IEventStore)store).Subject.ShouldBe(new Uri(expected));
    }

    /// <summary>
    ///     Regression guard — an ancillary store still resolves its subject from the MARKER TYPE rather
    ///     than from the name, which is what #5039's closed-generic handling depends on.
    /// </summary>
    [Fact]
    public async Task an_ancillary_store_keeps_its_marker_subject()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddMartenStore<ISubjectStore>(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "subject_ancillary";
            })).StartAsync();

        var store = host.Services.GetRequiredService<ISubjectStore>();

        ((IEventStore)store).Subject.ShouldBe(new Uri("marten://isubjectstore"));
    }

    /// <summary>
    ///     The second half of #5409: the name was assigned again after the
    ///     <c>IConfigureMarten&lt;T&gt;</c> chain, so this override was silently reverted.
    /// </summary>
    [Fact]
    public async Task a_contribution_can_name_an_ancillary_store()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMartenStore<INamedByContributionStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "subject_contribution";
                });

                services.ConfigureMarten<INamedByContributionStore>(opts => opts.StoreName = "Chosen");
            }).StartAsync();

        var store = host.Services.GetRequiredService<INamedByContributionStore>();

        // Asserted through Identity rather than through Options, because Identity is the public
        // surface StoreName feeds and IDocumentStore.Options is the read-only view.
        ((IEventStore)store).Identity.Name.ShouldBe("chosen");
        ((IEventStore)store).Subject.ShouldBe(new Uri("marten://inamedbycontributionstore"));
    }

    /// <summary>
    ///     Regression guard — with no contribution the marker type still names the store.
    /// </summary>
    [Fact]
    public async Task an_ancillary_store_is_named_after_its_marker_by_default()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddMartenStore<IDefaultNamedStore>(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "subject_default_named";
            })).StartAsync();

        var store = host.Services.GetRequiredService<IDefaultNamedStore>();

        ((IEventStore)store).Identity.Name.ShouldBe(nameof(IDefaultNamedStore).ToLowerInvariant());
    }
}

public interface ISubjectStore : IDocumentStore;

public interface INamedByContributionStore : IDocumentStore;

public interface IDefaultNamedStore : IDocumentStore;
