using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.Components.DictionaryAdapter;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Weasel.Core;
using Xunit;
using IRevisioned = Marten.Metadata.IRevisioned;

namespace DocumentDbTests.Concurrency;

public class numeric_revisioning: OneOffConfigurationsContext
{

    [Fact]
    public void use_numeric_revisions_is_off_by_default()
    {
        var mapping = (DocumentMapping)theStore.Options.Storage.FindMapping(typeof(Target));
        mapping.UseNumericRevisions.ShouldBeFalse();
        mapping.Metadata.Revision.Enabled.ShouldBeFalse();
    }


    [Fact]
    public void using_fluent_interface()
    {
        StoreOptions(opts => opts.Schema.For<Target>().UseNumericRevisions(true));

        var mapping = (DocumentMapping)theStore.Options.Storage.FindMapping(typeof(Target));
        mapping.Metadata.Revision.Enabled.ShouldBeTrue();
        mapping.UseNumericRevisions.ShouldBeTrue();
    }

    [Fact]
    public void decorate_int_property_with_Version_attribute()
    {
        var mapping = (DocumentMapping)theStore.Options.Storage.FindMapping(typeof(OtherRevisionedDoc));
        mapping.Metadata.Revision.Enabled.ShouldBeTrue();
        mapping.UseNumericRevisions.ShouldBeTrue();
        mapping.Metadata.Revision.Member.Name.ShouldBe("Version");
    }

    [Fact]
    public void infer_configuration_from_IRevisioned_interface()
    {
        var mapping = (DocumentMapping)theStore.Options.Storage.FindMapping(typeof(RevisionedDoc));
        mapping.Metadata.Revision.Enabled.ShouldBeTrue();
        mapping.UseNumericRevisions.ShouldBeTrue();
        mapping.Metadata.Revision.Member.Name.ShouldBe("Version");
    }

    [Fact]
    public async Task use_mapped_property_for_numeric_versioning()
    {
        using var store = SeparateStore(_ =>
        {
            _.Schema.For<UnconventionallyVersionedDoc>().UseNumericRevisions(true).Metadata(m =>
            {
                m.Revision.MapTo(x => x.UnconventionalVersion);
            });
        });
        store.StorageFeatures.MappingFor(typeof(UnconventionallyVersionedDoc))
            .Metadata.Revision.Member.Name.ShouldBe(nameof(UnconventionallyVersionedDoc.UnconventionalVersion));

        var session = store.LightweightSession();
        var doc = new UnconventionallyVersionedDoc{Id = Guid.NewGuid(), Name = "Initial Name"};

        session.Insert(doc);
        await session.SaveChangesAsync();

        var loaded = await session.LoadAsync<UnconventionallyVersionedDoc>(doc.Id);
        loaded.UnconventionalVersion.ShouldBe(1);

        doc.Name = "New Name";

        session.Store(doc);
        await session.SaveChangesAsync();

        loaded = await session.LoadAsync<UnconventionallyVersionedDoc>(doc.Id);
        loaded.UnconventionalVersion.ShouldBe(2);
    }

    [Fact]
    public async Task happy_path_insert()
    {
        var doc = new RevisionedDoc { Name = "Tim" };
        theSession.Insert(doc);
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<RevisionedDoc>(doc.Id);
        loaded.Version.ShouldBe(1);

        doc.Version.ShouldBe(1);
    }

    [Fact]
    public async Task fetch_document_metadata()
    {
        var doc = new RevisionedDoc { Name = "Tim" };
        theSession.Insert(doc);
        await theSession.SaveChangesAsync();

        var metadata = await theSession.MetadataForAsync(doc);
        metadata.CurrentRevision.ShouldBe(1);
    }

    [Fact]
    public async Task bulk_inserts()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        var doc2 = new RevisionedDoc { Name = "Molly" };
        var doc3 = new RevisionedDoc { Name = "JD" };

        await theStore.BulkInsertDocumentsAsync(new[] { doc1, doc2, doc3 });

        (await theSession.MetadataForAsync(doc1)).CurrentRevision.ShouldBe(1);
        (await theSession.MetadataForAsync(doc2)).CurrentRevision.ShouldBe(1);
        (await theSession.MetadataForAsync(doc3)).CurrentRevision.ShouldBe(1);

        (await theSession.LoadAsync<RevisionedDoc>(doc1.Id)).ShouldNotBeNull();
        (await theSession.LoadAsync<RevisionedDoc>(doc2.Id)).ShouldNotBeNull();
        (await theSession.LoadAsync<RevisionedDoc>(doc3.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task store_with_no_revision_from_start_succeeds_with_revision_1()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        (await theSession.MetadataForAsync(doc1)).CurrentRevision.ShouldBe(1);
        (await theSession.LoadAsync<RevisionedDoc>(doc1.Id)).Version.ShouldBe(1);
    }

    [Fact]
    public async Task store_twice_with_no_version_can_override()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();



        theSession.Store(new RevisionedDoc{Id = doc1.Id, Name = "Brad"});
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<RevisionedDoc>(doc1.Id)).Name.ShouldBe("Brad");
    }

    [Fact]
    public async Task optimistic_concurrency_failure_with_update_revision()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Bill";
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Dru";
        doc1.Version = 3;
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        var doc2 = new RevisionedDoc { Id = doc1.Id, Name = "Wrong" };
        theSession.UpdateRevision(doc2, doc1.Version + 1);
        await theSession.SaveChangesAsync();



        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            theSession.UpdateRevision(doc2, 2);
            await theSession.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task optimistic_concurrency_miss_with_try_update_revision()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Bill";
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Dru";
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        var doc2 = new RevisionedDoc { Id = doc1.Id, Name = "Tron" };
        theSession.UpdateRevision(doc2, doc1.Version + 1);
        await theSession.SaveChangesAsync();

        // No failure
        theSession.TryUpdateRevision(doc2, 2);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<RevisionedDoc>(doc1.Id)).Name.ShouldBe("Tron");
    }

    [Fact]
    public async Task update_just_overwrites_and_increments_version()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Bill";
        doc1.Version = 0;
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Dru";
        doc1.Version = 0;
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();



        var doc2 = new RevisionedDoc { Id = doc1.Id, Name = "Wrong", Version = 0};
        theSession.UpdateRevision(doc2, doc1.Version + 1);
        await theSession.SaveChangesAsync();

        doc2.Name = "Last";
        doc2.Version = 0;
        theSession.Update(doc2);
        await theSession.SaveChangesAsync();

        var doc3 = await theSession.LoadAsync<RevisionedDoc>(doc1.Id);
        doc2.Version = 0;
        doc3.Name.ShouldBe("Last");
        doc3.Version.ShouldBe(5);

        (await theSession.MetadataForAsync(doc1)).CurrentRevision.ShouldBe(5);


    }

    [Fact]
    public async Task update_revision_and_jumping_multiples()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Bill";
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Dru";
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        var doc2 = new RevisionedDoc { Id = doc1.Id, Name = "Tron", Version = 2};
        theSession.UpdateRevision(doc2, 10);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<RevisionedDoc>(doc1.Id)).Name.ShouldBe("Tron");
        (await theSession.MetadataForAsync(doc2)).CurrentRevision.ShouldBe(10);
    }

    [Fact]
    public async Task overwrite_increments_version()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Bill";
        doc1.Version = 0;
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Dru";
        doc1.Version = 0;
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        var doc2 = new RevisionedDoc { Id = doc1.Id, Name = "Wrong", Version = 2};
        theSession.UpdateRevision(doc2, doc1.Version + 1);
        await theSession.SaveChangesAsync();

        using var session2 =
            theStore.OpenSession(new SessionOptions { ConcurrencyChecks = ConcurrencyChecks.Disabled });

        doc2.Name = "Last";
        doc2.Version = 0;
        session2.Store(doc2);

        await session2.SaveChangesAsync();

        var doc3 = await session2.LoadAsync<RevisionedDoc>(doc1.Id);
        doc3.Name.ShouldBe("Last");
        doc3.Version.ShouldBe(5);

        (await session2.MetadataForAsync(doc1)).CurrentRevision.ShouldBe(5);


    }


    [Fact]
    public async Task load_and_update_revisioned_document_from_identity_map_session()
    {
        var doc1 = new RevisionedDoc();
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        using var session = theStore.IdentitySession();
        var doc2 = await session.LoadAsync<RevisionedDoc>(doc1.Id);

        doc2.Version++;
        doc2.Name = "Different";
        session.Update(doc2);
        await session.SaveChangesAsync();
    }


    [Fact]
    public async Task load_and_update_revisioned_document_by_revision_from_identity_map_session()
    {
        var doc1 = new RevisionedDoc();
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        using var session = theStore.IdentitySession();
        var doc2 = await session.LoadAsync<RevisionedDoc>(doc1.Id);

        doc2.Name = "Different";
        session.UpdateRevision(doc2, 2);


        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task load_and_update_revisioned_document_from_dirty_session()
    {
        var doc1 = new RevisionedDoc();
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        using var session = theStore.DirtyTrackedSession();
        var doc2 = await session.LoadAsync<RevisionedDoc>(doc1.Id);

        doc2.Version++;
        doc2.Name = "Different";
        session.Update(doc2);
        await session.SaveChangesAsync();
    }


    [Fact]
    public async Task load_and_update_revisioned_document_by_revision_from_dirty_session()
    {
        var doc1 = new RevisionedDoc();
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        using var session = theStore.DirtyTrackedSession();
        var doc2 = await session.LoadAsync<RevisionedDoc>(doc1.Id);

        doc2.Name = "Different";
        session.UpdateRevision(doc2, 2);
        await session.SaveChangesAsync();
    }



    [Fact]
    public async Task optimistic_concurrency_failure_with_update_revision_when_revision_number_equal_in_new_doc_and_db()
    {
        StoreOptions(opts =>
        {
            opts.GeneratedCodeMode = TypeLoadMode.Auto;
            opts.GeneratedCodeOutputPath = AppContext.BaseDirectory.ParentDirectory().ParentDirectory()
                .ParentDirectory().AppendPath("Internal", "Generated");
        });

        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Bill";
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Dru";
        doc1.Version = 3;
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        var doc2 = new RevisionedDoc { Id = doc1.Id, Name = "Wrong" };
        theSession.UpdateRevision(doc2, doc1.Version + 1);
        await theSession.SaveChangesAsync();



        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            theSession.UpdateRevision(doc2, doc1.Version + 1);
            await theSession.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task optimistic_concurrency_failure_with_update_revision_when_revision_number_equal_in_the_same_doc_and_db()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Bill";
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Dru";
        doc1.Version = 3;
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        var doc2 = new RevisionedDoc { Id = doc1.Id, Name = "Wrong" };
        theSession.UpdateRevision(doc2, doc1.Version + 1);
        await theSession.SaveChangesAsync();



        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            theSession.UpdateRevision(doc1, doc1.Version + 1);
            await theSession.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task concurrent_update_revision_does_not_silently_lose_writes()
    {
        // Regression: under READ COMMITTED contention, mt_upsert_* for a numeric-revisioned
        // document could return a non-zero final_version even when its ON CONFLICT ... DO UPDATE
        // ... WHERE revision > mt_version clause silently skipped the row update because a
        // concurrent transaction had already bumped mt_version. SaveChangesAsync would not throw,
        // AfterCommitAsync would fire, and the caller would believe the write landed.
        //
        // Invariant: when N sessions all load the same doc at v=1 and race UpdateRevision(doc, 2),
        // exactly one SaveChanges should succeed and the rest must throw ConcurrencyException.

        var listener = new CommitCountingListener();
        StoreOptions(opts =>
        {
            opts.Schema.For<RevisionedDoc>().UseNumericRevisions(true);
            opts.Listeners.Add(listener);
        });

        const int N = 50;

        var doc = new RevisionedDoc { Name = "Initial" };
        await using (var seed = theStore.LightweightSession())
        {
            seed.Store(doc);
            await seed.SaveChangesAsync();
        }

        // Warm the connection pool so all N connectors are established before the race.
        // With a cold pool, connection setup serializes and stretches the timing so the
        // race window doesn't open; once warm, every run reproduces.
        await Task.WhenAll(Enumerable.Range(0, N).Select(async _ =>
        {
            await using var warm = theStore.QuerySession();
            await warm.LoadAsync<RevisionedDoc>(doc.Id);
        }));
        listener.Reset();

        var sessions = new List<IDocumentSession>(N);
        try
        {
            for (var i = 0; i < N; i++)
            {
                var session = theStore.LightweightSession();
                var loaded = await session.LoadAsync<RevisionedDoc>(doc.Id);
                loaded.Version.ShouldBe(1);
                loaded.Name = $"session {i}";
                session.UpdateRevision(loaded, 2);
                sessions.Add(session);
            }

            var successes = 0;
            var concurrencyFailures = 0;
            await Parallel.ForEachAsync(sessions,
                new ParallelOptions { MaxDegreeOfParallelism = N },
                async (session, ct) =>
                {
                    try
                    {
                        await Task.Delay(Random.Shared.Next(0, 10), ct);
                        await session.SaveChangesAsync(ct);
                        Interlocked.Increment(ref successes);
                    }
                    catch (ConcurrencyException)
                    {
                        Interlocked.Increment(ref concurrencyFailures);
                    }
                });

            await using var verify = theStore.QuerySession();
            var persisted = await verify.LoadAsync<RevisionedDoc>(doc.Id);
            persisted.Version.ShouldBe(2);

            // Only one writer can move v=1 → v=2. Every other SaveChanges must surface
            // as a ConcurrencyException, never as a silent success.
            successes.ShouldBe(1, "silent writes detected");
            concurrencyFailures.ShouldBe(N - 1);

            // AfterCommitAsync must not fire for sessions whose write was silently dropped.
            listener.AfterCommit.ShouldBe(successes);
            listener.BeforeSave.ShouldBe(N);
        }
        finally
        {
            foreach (var session in sessions) session.Dispose();
        }
    }



}

internal class CommitCountingListener: DocumentSessionListenerBase
{
    private int _beforeSave;
    private int _afterCommit;

    public int BeforeSave => _beforeSave;
    public int AfterCommit => _afterCommit;

    public void Reset()
    {
        Interlocked.Exchange(ref _beforeSave, 0);
        Interlocked.Exchange(ref _afterCommit, 0);
    }

    public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
    {
        Interlocked.Increment(ref _beforeSave);
        return Task.CompletedTask;
    }

    public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        Interlocked.Increment(ref _afterCommit);
        return Task.CompletedTask;
    }
}



public class RevisionedDoc: IRevisioned
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public int Version { get; set; }
}

public class OtherRevisionedDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    [Version]
    public int Version { get; set; }
}

public class UnconventionallyVersionedDoc
{
    public Guid Id { get; set; }

    public string Name { get; set; }

    public int UnconventionalVersion { get; set; }
}
