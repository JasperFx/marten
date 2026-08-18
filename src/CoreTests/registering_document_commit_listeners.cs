using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Documents;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace CoreTests;

public interface ICommitListenerAncillaryStore: IDocumentStore;

public class RecordingCommitListener: IDocumentCommitListener
{
    private readonly List<IDocumentChangeSet> _commits = new();

    public IReadOnlyList<IDocumentChangeSet> Commits
    {
        get
        {
            lock (_commits) return _commits.ToArray();
        }
    }

    public Task AfterCommitAsync(IDocumentSessionOperations session, IDocumentChangeSet commit, CancellationToken token)
    {
        lock (_commits) _commits.Add(commit);
        return Task.CompletedTask;
    }
}

/// <summary>
/// jasperfx#679 (#5258). The container half of the IDocumentCommitListener adoption.
/// </summary>
/// <remarks>
/// <para>
/// DocumentCommitListenerCompliance pins the behavior — that a registered listener fires, with a
/// materialized change set — but it structurally CANNOT cover any of this, and that is worth being
/// explicit about rather than leaving as an accident of where the tests live.
/// MartenDocumentComplianceFixture builds a bare <c>new StoreOptions()</c> and replays the suite's
/// listeners through <see cref="StoreOptions.AddCommitListener" /> by hand. No container is involved
/// at any point, so the sweep in <c>AddMarten</c> — the thing an actual application depends on — is
/// exercised by nothing over there.
/// </para>
/// <para>
/// The ancillary facts below are the deliberate boundary rather than a limitation. A bare
/// <c>GetServices&lt;IDocumentCommitListener&gt;()</c> from the StoreOptions factory cannot tell
/// which store a registration was meant for, so sweeping it onto every AddMartenStore&lt;T&gt; in
/// the application would attach every listener to every store with no way to opt one out. Primary
/// store only, matching what IInitialData has always done; ancillary stores opt in explicitly.
/// </para>
/// </remarks>
public class registering_document_commit_listeners: HostedStoreContext
{
    [Fact]
    public async Task container_registered_listener_is_swept_onto_the_primary_store()
    {
        var listener = new RecordingCommitListener();

        var host = await StartHostAsync(_ => { },
            configureServices: services => services.AddSingleton<IDocumentCommitListener>(listener));

        var store = StoreOf(host);

        // Asserted on the options as well as end-to-end below, because the two fail for different
        // reasons: an empty Listeners collection means the sweep never ran, while a populated one
        // with no recorded commit means the adapter is not forwarding.
        store.Options.Listeners.OfType<IDocumentSessionListener>().ShouldNotBeEmpty();

        await using var session = store.LightweightSession();
        session.Store(Target.Random());
        await session.SaveChangesAsync();

        var commit = listener.Commits.ShouldHaveSingleItem();
        commit.Inserted.Concat(commit.Updated).ShouldHaveSingleItem().ShouldBeOfType<Target>();
    }

    [Fact]
    public async Task every_container_registered_listener_is_swept()
    {
        var first = new RecordingCommitListener();
        var second = new RecordingCommitListener();

        var host = await StartHostAsync(_ => { },
            configureServices: services =>
            {
                services.AddSingleton<IDocumentCommitListener>(first);
                services.AddSingleton<IDocumentCommitListener>(second);
            });

        await using var session = StoreOf(host).LightweightSession();
        session.Store(Target.Random());
        await session.SaveChangesAsync();

        // GetServices, not GetService: a sweep written with the singular would forward only the
        // last registration and would pass every fact above.
        first.Commits.Count.ShouldBe(1);
        second.Commits.Count.ShouldBe(1);
    }

    [Fact]
    public async Task the_sweep_does_NOT_reach_an_ancillary_store()
    {
        var listener = new RecordingCommitListener();

        var host = await StartHostAsync(_ => { },
            configureServices: services =>
            {
                services.AddSingleton<IDocumentCommitListener>(listener);
                services.AddMartenStore<ICommitListenerAncillaryStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DisableNpgsqlLogging = true;
                    opts.DatabaseSchemaName = $"{SchemaName}_ancillary";
                });
            });

        var ancillary = host.Services.GetRequiredService<ICommitListenerAncillaryStore>();

        await using var session = ancillary.LightweightSession();
        session.Store(Target.Random());
        await session.SaveChangesAsync();

        // The documented boundary. If this ever goes green the other way round, it is a behavior
        // change for every application running more than one Marten store, not a bug fix.
        listener.Commits.ShouldBeEmpty();
    }

    [Fact]
    public async Task an_ancillary_store_opts_in_through_ConfigureMarten()
    {
        var listener = new RecordingCommitListener();

        var host = await StartHostAsync(_ => { },
            configureServices: services =>
            {
                services.AddMartenStore<ICommitListenerAncillaryStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DisableNpgsqlLogging = true;
                    opts.DatabaseSchemaName = $"{SchemaName}_optin";
                });

                services.ConfigureMarten<ICommitListenerAncillaryStore>(opts => opts.AddCommitListener(listener));
            });

        var ancillary = host.Services.GetRequiredService<ICommitListenerAncillaryStore>();

        await using var session = ancillary.LightweightSession();
        session.Store(Target.Random());
        await session.SaveChangesAsync();

        listener.Commits.ShouldHaveSingleItem()
            .Inserted.Concat(listener.Commits[0].Updated)
            .ShouldHaveSingleItem().ShouldBeOfType<Target>();
    }

    [Fact]
    public async Task a_deletion_is_reported_by_type_and_identity()
    {
        var listener = new RecordingCommitListener();

        var host = await StartHostAsync(_ => { },
            configureServices: services => services.AddSingleton<IDocumentCommitListener>(listener));

        var store = StoreOf(host);
        var target = Target.Random();

        await using (var session = store.LightweightSession())
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Delete(target);
            await session.SaveChangesAsync();
        }

        listener.Commits.Count.ShouldBe(2);

        var deletion = listener.Commits[1].Deleted.ShouldHaveSingleItem();
        deletion.DocumentType.ShouldBe(typeof(Target));
        deletion.Id.ShouldBe(target.Id);
    }
}
