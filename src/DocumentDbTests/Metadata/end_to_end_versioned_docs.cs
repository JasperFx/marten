using System;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Services.Json;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Metadata;

public class end_to_end_versioned_docs: IntegrationContext
{
    [Fact]
    public async Task save_initial_version_of_the_doc_and_see_the_initial_version_assigned()
    {
        var doc = new AttVersionedDoc();

        using var session = theStore.LightweightSession();
        session.Store(doc);

        session.VersionFor(doc).ShouldBeNull();

        await session.SaveChangesAsync();

        session.VersionFor(doc).ShouldNotBeNull();
        doc.Version.ShouldNotBe(Guid.Empty);
        doc.Version.ShouldBe(session.VersionFor(doc).Value);
    }

    [Fact]
    public async Task save_initial_version_of_the_doc_and_see_the_initial_version_assigned_async()
    {
        var doc = new AttVersionedDoc();

        await using var session = theStore.LightweightSession();
        session.Store(doc);

        session.VersionFor(doc).ShouldBeNull();

        await session.SaveChangesAsync();

        session.VersionFor(doc).ShouldNotBeNull();
        doc.Version.ShouldNotBe(Guid.Empty);
        doc.Version.ShouldBe(session.VersionFor(doc).Value);
    }

    [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
    public async Task overwrite_behavior()
    {
        var originalVerion = Guid.Empty;
        var doc = new AttVersionedDoc();
        using (var session = theStore.LightweightSession())
        {
            session.Store(doc);

            session.VersionFor(doc).ShouldBeNull();

            await session.SaveChangesAsync();

            originalVerion = doc.Version;
        }

        IDocumentSession session1 = null;
        IDocumentSession session2 = null;

        try
        {
            session1 = theStore.LightweightSession();
            session2 = theStore.LightweightSession(new SessionOptions
            {
                ConcurrencyChecks = ConcurrencyChecks.Disabled
            });

            var doc1 = session1.Load<AttVersionedDoc>(doc.Id);
            doc1.Version.ShouldBe(originalVerion);
            session1.VersionFor(doc1).ShouldBe(originalVerion);
            session1.Store(doc1);

            var doc2 = session2.Load<AttVersionedDoc>(doc.Id);
            session2.Store(doc2);

            // save via session1
            await session1.SaveChangesAsync();
            doc1.Version.ShouldNotBe(originalVerion);

            // overwrite successfully w/ session2
            await session2.SaveChangesAsync();
            doc2.Version.ShouldNotBe(originalVerion);
            doc2.Version.ShouldNotBe(doc1.Version);
        }
        finally
        {
            session1?.Dispose();
            session2?.Dispose();
        }
    }

    [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
    public async Task overwrite_behavior_with_props()
    {
        var originalVerion = Guid.Empty;
        var doc = new PropVersionedDoc();
        using (var session = theStore.LightweightSession())
        {
            session.Store(doc);

            session.VersionFor(doc).ShouldBeNull();

            await session.SaveChangesAsync();

            originalVerion = doc.Version;
        }

        IDocumentSession session1 = null;
        IDocumentSession session2 = null;

        try
        {
            session1 = theStore.LightweightSession();
            session2 = theStore.LightweightSession(new SessionOptions
            {
                ConcurrencyChecks = ConcurrencyChecks.Disabled
            });

            var doc1 = session1.Load<PropVersionedDoc>(doc.Id);
            doc1.Version.ShouldBe(originalVerion);
            session1.VersionFor(doc1).ShouldBe(originalVerion);
            session1.Store(doc1);

            var doc2 = session2.Load<PropVersionedDoc>(doc.Id);
            session2.Store(doc2);

            // save via session1
            await session1.SaveChangesAsync();
            doc1.Version.ShouldNotBe(originalVerion);

            // overwrite successfully w/ session2
            await session2.SaveChangesAsync();
            doc2.Version.ShouldNotBe(originalVerion);
            doc2.Version.ShouldNotBe(doc1.Version);
        }
        finally
        {
            session1?.Dispose();
            session2?.Dispose();
        }
    }

    [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
    public async Task overwrite_behavior_async()
    {
        var originalVerion = Guid.Empty;
        var doc = new AttVersionedDoc();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(doc);

            session.VersionFor(doc).ShouldBeNull();

            await session.SaveChangesAsync();

            originalVerion = doc.Version;
        }

        IDocumentSession session1 = null;
        IDocumentSession session2 = null;

        try
        {
            session1 = theStore.LightweightSession();
            session2 = theStore.LightweightSession(new SessionOptions
            {
                ConcurrencyChecks = ConcurrencyChecks.Disabled
            });

            var doc1 = await session1.LoadAsync<AttVersionedDoc>(doc.Id);
            doc1.Version.ShouldBe(originalVerion);
            session1.VersionFor(doc1).ShouldBe(originalVerion);
            session1.Store(doc1);

            var doc2 = await session2.LoadAsync<AttVersionedDoc>(doc.Id);
            session2.Store(doc2);

            // save via session1
            await session1.SaveChangesAsync();
            doc1.Version.ShouldNotBe(originalVerion);

            // overwrite successfully w/ session2
            await session2.SaveChangesAsync();
            doc2.Version.ShouldNotBe(originalVerion);
            doc2.Version.ShouldNotBe(doc1.Version);
        }
        finally
        {
            session1?.Dispose();
            session2?.Dispose();
        }
    }

    public end_to_end_versioned_docs(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
