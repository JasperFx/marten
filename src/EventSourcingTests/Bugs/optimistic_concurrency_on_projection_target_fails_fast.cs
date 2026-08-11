using System;
using EventSourcingTests.Aggregation;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Metadata;
using Marten.Schema;
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
///
/// <para>
/// #5159 narrowed that guard. <c>UseOptimisticConcurrency(true)</c> now clears the competing
/// numeric-revision metadata the same way <c>UseNumericRevisions(true)</c> has always cleared
/// the Guid version, so for a plain document the fluent pair is order-independent and the last
/// call wins. The guard is kept — with a message that says which case it is — only where the
/// override genuinely cannot work: a projection target, or a revision bound to a member on the
/// user's own type.
/// </para>
/// </summary>
public class optimistic_concurrency_on_projection_target_fails_fast: BugIntegrationContext
{
    [Fact]
    public void use_optimistic_concurrency_on_projected_document_throws_invalid_document_exception()
    {
        // ProjectionDocumentPolicy forces MyAggregate onto numeric revisions because it is the
        // target of a projection, and the projection machinery writes the aggregate version into
        // that column. Pushing it onto Guid concurrency can never work, so this must still fail
        // fast — here already at store bootstrap, because the projection's ValidateConfiguration
        // materializes the aggregate mapping.
        var ex = Should.Throw<InvalidDocumentException>(() => StoreOptions(opts =>
        {
            opts.Projections.Add<AllGood>(ProjectionLifecycle.Inline);
            opts.Schema.For<MyAggregate>().UseOptimisticConcurrency(true);
        }));

        ex.Message.ShouldContain(nameof(MyAggregate));
        ex.Message.ShouldContain("projection");
        ex.Message.ShouldContain("UseOptimisticConcurrency");
    }

    [Fact]
    public void plain_document_numeric_revisions_then_optimistic_concurrency_is_last_wins()
    {
        // #5159: this order used to throw while the reverse order quietly worked. Nothing about
        // Target says "numeric revisions" other than the first call, so the second call overriding
        // it is exactly what the fluent API reads like it should do.
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().UseNumericRevisions(true);
            opts.Schema.For<Target>().UseOptimisticConcurrency(true);
        });

        var mapping = theStore.Options.Storage.MappingFor(typeof(Target));

        mapping.UseOptimisticConcurrency.ShouldBeTrue();
        mapping.UseNumericRevisions.ShouldBeFalse();
        mapping.Metadata.Version.Enabled.ShouldBeTrue();
        mapping.Metadata.Revision.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void plain_document_optimistic_concurrency_then_numeric_revisions_is_last_wins()
    {
        // The mirror image, which has always worked. Pinned so the two orders cannot drift apart
        // again — the asymmetry between them is the whole of #5159.
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().UseOptimisticConcurrency(true);
            opts.Schema.For<Target>().UseNumericRevisions(true);
        });

        var mapping = theStore.Options.Storage.MappingFor(typeof(Target));

        mapping.UseNumericRevisions.ShouldBeTrue();
        mapping.UseOptimisticConcurrency.ShouldBeFalse();
        mapping.Metadata.Revision.Enabled.ShouldBeTrue();
        mapping.Metadata.Version.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void interface_driven_revisions_plus_optimistic_concurrency_fails_fast()
    {
        // Still an error, and deliberately so. IRevisioned is a declaration on the user's own
        // type rather than something Marten inferred: honoring the override would leave
        // RevisionedDoc.Version unmapped and silently never populated. The message has to name
        // the member so that is obvious.
        StoreOptions(opts => opts.Schema.For<RevisionedDoc>().UseOptimisticConcurrency(true));

        var ex = Should.Throw<InvalidDocumentException>(
            () => theStore.Options.Storage.MappingFor(typeof(RevisionedDoc)));

        ex.Message.ShouldContain(nameof(RevisionedDoc));
        ex.Message.ShouldContain(nameof(IRevisioned.Version));
    }

    [Fact]
    public void long_version_member_plus_optimistic_concurrency_fails_fast()
    {
        // Same rule by the other route onto Metadata.Revision.Member: a long [Version] member
        // declares the revision on the document type just as IRevisioned does.
        StoreOptions(opts => opts.Schema.For<LongVersionedDoc>().UseOptimisticConcurrency(true));

        var ex = Should.Throw<InvalidDocumentException>(
            () => theStore.Options.Storage.MappingFor(typeof(LongVersionedDoc)));

        ex.Message.ShouldContain(nameof(LongVersionedDoc));
        ex.Message.ShouldContain(nameof(LongVersionedDoc.Version));
    }

    public class RevisionedDoc: IRevisioned
    {
        public Guid Id { get; set; }
        public int Version { get; set; }
    }

    public class LongVersionedDoc
    {
        public Guid Id { get; set; }

        [Version] public long Version { get; set; }
    }
}
