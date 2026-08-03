using System;
using EventSourcingTests.Aggregation;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Metadata;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// Companion guard to Bug_2978: the Guid version column and the numeric revision column are
/// two flavors of the same physical <c>mt_version</c> column, so a mapping that ends up with
/// both enabled used to surface as a raw duplicate-key <c>ArgumentException</c> (or invalid
/// DDL with two <c>mt_version</c> columns) instead of an actionable configuration error.
/// </summary>
public class optimistic_concurrency_on_projection_target_fails_fast: BugIntegrationContext
{
    [Fact]
    public void use_optimistic_concurrency_on_projected_document_throws_invalid_document_exception()
    {
        // ProjectionDocumentPolicy forces MyAggregate onto numeric revisions, then the fluent
        // override runs after the policies and re-enables the Guid version — leaving both
        // flavors on. That combination can never work and must fail fast — here already at
        // store bootstrap, because the projection's ValidateConfiguration materializes the
        // aggregate mapping.
        var ex = Should.Throw<InvalidDocumentException>(() => StoreOptions(opts =>
        {
            opts.Projections.Add<AllGood>(ProjectionLifecycle.Inline);
            opts.Schema.For<MyAggregate>().UseOptimisticConcurrency(true);
        }));

        ex.Message.ShouldContain("UseOptimisticConcurrency");
        ex.Message.ShouldContain("UseNumericRevisions");
        ex.Message.ShouldContain(nameof(MyAggregate));
    }

    [Fact]
    public void switching_a_plain_document_from_numeric_revisions_to_optimistic_concurrency_also_fails_fast()
    {
        // Without any projection involved, stacking the two fluent calls leaves the revision
        // metadata enabled from the first call while the second re-enables the Guid version.
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().UseNumericRevisions(true);
            opts.Schema.For<Target>().UseOptimisticConcurrency(true);
        });

        Should.Throw<InvalidDocumentException>(
            () => theStore.Options.Storage.MappingFor(typeof(Target)));
    }

    [Fact]
    public void interface_driven_revisions_plus_optimistic_concurrency_fails_fast()
    {
        // The likeliest real-world route into the invalid state, and the one neither test above
        // covers: nothing in the configuration says "numeric revisions" — VersionedPolicy turns
        // them on because the document implements IRevisioned, and the fluent call then re-enables
        // the Guid version on top. Before the guard this reached the database as DDL with two
        // mt_version columns.
        StoreOptions(opts => opts.Schema.For<RevisionedDoc>().UseOptimisticConcurrency(true));

        var ex = Should.Throw<InvalidDocumentException>(
            () => theStore.Options.Storage.MappingFor(typeof(RevisionedDoc)));

        ex.Message.ShouldContain(nameof(RevisionedDoc));
    }

    public class RevisionedDoc: IRevisioned
    {
        public Guid Id { get; set; }
        public int Version { get; set; }
    }
}
