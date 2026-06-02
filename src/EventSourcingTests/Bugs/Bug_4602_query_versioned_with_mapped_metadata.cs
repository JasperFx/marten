using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// Companion coverage for #4602. The headline repro in
/// <see cref="Bug_4602_query_tenant_mapped_projection_document"/> frames the bug as
/// "projection target + tenant_id mapped", but the actual fault envelope is wider — and
/// the fix lives entirely in the QueryOnly selector path. Every probe below therefore
/// runs across <em>all four</em> selectors (<c>QueryOnly</c>, <c>Lightweight</c>,
/// <c>IdentityMap</c>, <c>DirtyTracking</c>):
/// <list type="bullet">
///   <item>The QueryOnly case proves the regression is fixed (was failing on master).</item>
///   <item>The Lightweight / IdentityMap / DirtyTracking cases are canaries — those
///     selectors were never broken (their SELECTs include <c>mt_version</c> for the
///     write-back path), so if the fix accidentally trimmed their binders too they'd
///     crash with the inverse mismatch. Pinning all four prevents a future "fix" from
///     unifying them and reintroducing the offset.</item>
/// </list>
///
/// <para>Bug envelope probed (beyond the headline repro): non-projection docs that
/// opt into <c>UseNumericRevisions</c>/<c>UseOptimisticConcurrency</c> manually, mapped
/// after-version metadata members other than <c>tenant_id</c>, and hierarchical
/// projection targets. All hit the same QueryOnly ordinal mismatch on master.</para>
/// </summary>
public partial class Bug_4602_query_versioned_with_mapped_metadata: BugIntegrationContext
{
    // ---- Trigger #1: UseNumericRevisions on a NON-projection doc, query across all 4 selectors ----

    [Theory]
    [ClassData(typeof(AllSelectorStyles))]
    public async Task non_projection_doc_with_numeric_revisions_and_mapped_tenant_id_can_be_queried(SelectorStyle style)
    {
        // No projection registered. The user opted into numeric revisions directly —
        // same downstream descriptor state (UseNumericRevisions=true, no Revision
        // member, version binder in readBinders, tenant binder after it) that the
        // projection-target path produces. The QueryOnly selector is the one that was
        // broken on master; the other three serve as canaries.
        StoreOptions(opts =>
        {
            opts.Schema.For<MappedDoc>()
                .UseNumericRevisions(true)
                .MultiTenanted()
                .Metadata(m => m.TenantId.MapTo(x => x.Tenant));
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession("tenant-a"))
        {
            session.Store(new MappedDoc { Id = id, Name = "n", Tenant = "tenant-a" });
            await session.SaveChangesAsync();
        }

        await using var query = OpenReadSession(style, "tenant-a");
        var docs = await query.Query<MappedDoc>().ToListAsync();
        docs.Count.ShouldBe(1, $"selector {style}");
        docs[0].Tenant.ShouldBe("tenant-a", $"selector {style} should read mapped tenant id");
    }

    // ---- Trigger #2: UseOptimisticConcurrency (Guid) — the sibling code path ----

    [Theory]
    [ClassData(typeof(AllSelectorStyles))]
    public async Task non_projection_doc_with_optimistic_concurrency_and_mapped_tenant_id_can_be_queried(SelectorStyle style)
    {
        // The Version branch of DocumentStorageDescriptorBuilder (lines 70-81) has the
        // exact same shape as the Revision branch the headline repro hits. If the fix
        // only addresses the Numeric path, this Guid-optimistic path stays broken on
        // QueryOnly. Run across all 4 selectors for the same reason as #1.
        StoreOptions(opts =>
        {
            opts.Schema.For<MappedDoc>()
                .UseOptimisticConcurrency(true)
                .MultiTenanted()
                .Metadata(m => m.TenantId.MapTo(x => x.Tenant));
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession("tenant-a"))
        {
            session.Store(new MappedDoc { Id = id, Name = "n", Tenant = "tenant-a" });
            await session.SaveChangesAsync();
        }

        await using var query = OpenReadSession(style, "tenant-a");
        var docs = await query.Query<MappedDoc>().ToListAsync();
        docs.Count.ShouldBe(1, $"selector {style}");
        docs[0].Tenant.ShouldBe("tenant-a", $"selector {style} should read mapped tenant id");
    }

    // ---- Trigger #3: tenant_id isn't required — any after-version mapped member shifts ----

    [Theory]
    [ClassData(typeof(AllSelectorStyles))]
    public async Task projection_target_with_mapped_last_modified_can_be_queried(SelectorStyle style)
    {
        // No tenancy at all — mapped LastModified is enough to shift the QueryOnly
        // binder walk. If a fix is overly narrow ("only DocumentTenantIdBinder"), this
        // stays broken.
        StoreOptions(opts =>
        {
            opts.Projections.Add<LastModifiedProjection>(ProjectionLifecycle.Inline);
            opts.Schema.For<LastModifiedDoc>()
                .Metadata(m => m.LastModified.MapTo(x => x.LastModified));
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<LastModifiedDoc>(id, new LastModifiedCreated(id, "lm"));
            await session.SaveChangesAsync();
        }

        await using var query = OpenReadSession(style, tenantId: null);
        var docs = await query.Query<LastModifiedDoc>().ToListAsync();
        docs.Count.ShouldBe(1, $"selector {style}");
        docs[0].LastModified.ShouldNotBe(default, $"selector {style} should read mapped last-modified");
    }

    [Theory]
    [ClassData(typeof(AllSelectorStyles))]
    public async Task non_projection_doc_with_numeric_revisions_and_mapped_correlation_id_can_be_queried(SelectorStyle style)
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<CorrelationDoc>()
                .UseNumericRevisions(true)
                .Metadata(m => m.CorrelationId.MapTo(x => x.CorrelationId));
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.CorrelationId = "corr-a";
            session.Store(new CorrelationDoc { Id = id, Name = "corr" });
            await session.SaveChangesAsync();
        }

        await using var query = OpenReadSession(style, tenantId: null);
        var docs = await query.Query<CorrelationDoc>().ToListAsync();
        docs.Count.ShouldBe(1, $"selector {style}");
        docs[0].CorrelationId.ShouldBe("corr-a", $"selector {style} should read mapped correlation id");
    }

    // ---- Trigger #4: hierarchical projection target ----

    [Theory]
    [ClassData(typeof(AllSelectorStyles))]
    public async Task hierarchical_projection_target_with_mapped_tenant_id_can_be_queried(SelectorStyle style)
    {
        // doc_type binder sits BEFORE the version binder in readBinders (and is always
        // in the QueryOnly SELECT — DocTypeColumn has no QueryOnly guard), but the
        // version → tenant_id shift still trips QueryOnly. The reported error bound
        // on master is "0 and 2" (vs "0 and 1") because the SELECT is one column wider.
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Add<HierBaseProjection>(ProjectionLifecycle.Inline);
            opts.Schema.For<HierBase>()
                .AddSubClass<HierChild>()
                .MultiTenanted()
                .Metadata(m => m.TenantId.MapTo(x => x.Tenant));
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession("tenant-a"))
        {
            session.Events.StartStream<HierBase>(id, new HierCreated(id, "hier", true));
            await session.SaveChangesAsync();
        }

        await using var query = OpenReadSession(style, "tenant-a");
        var docs = await query.Query<HierBase>().ToListAsync();
        docs.Count.ShouldBe(1, $"selector {style}");
        docs[0].Tenant.ShouldBe("tenant-a", $"selector {style} should read mapped tenant id");
    }

    // ---- Theory infrastructure ----

    /// <summary>
    /// The four read-path storage selectors a session can return. Each one is a
    /// separate <c>IDocumentStorage&lt;T&gt;</c> subclass under
    /// <c>Marten.Internal.ClosedShape</c>; the bug surfaces only on
    /// <see cref="QueryOnly"/> but the fix has to leave the other three byte-identical.
    /// </summary>
    public enum SelectorStyle
    {
        QueryOnly,
        Lightweight,
        IdentityMap,
        DirtyTracking
    }

    /// <summary>
    /// Open a read-capable session for the given selector. The four flavors route to
    /// distinct <c>ClosedShape*Selector</c> implementations under the hood (see
    /// <c>DocumentProvider</c>'s tracking-style switch).
    /// </summary>
    private IQuerySession OpenReadSession(SelectorStyle style, string? tenantId)
    {
        return style switch
        {
            SelectorStyle.QueryOnly =>
                tenantId is null ? theStore.QuerySession() : theStore.QuerySession(tenantId),
            SelectorStyle.Lightweight =>
                tenantId is null ? theStore.LightweightSession() : theStore.LightweightSession(tenantId),
            SelectorStyle.IdentityMap =>
                tenantId is null ? theStore.IdentitySession() : theStore.IdentitySession(tenantId),
            SelectorStyle.DirtyTracking =>
                tenantId is null ? theStore.DirtyTrackedSession() : theStore.DirtyTrackedSession(tenantId),
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };
    }

    public class AllSelectorStyles: IEnumerable<object[]>
    {
        public IEnumerator<object[]> GetEnumerator()
        {
            yield return new object[] { SelectorStyle.QueryOnly };
            yield return new object[] { SelectorStyle.Lightweight };
            yield return new object[] { SelectorStyle.IdentityMap };
            yield return new object[] { SelectorStyle.DirtyTracking };
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // ---- types ----

    public record LastModifiedCreated(Guid Id, string Name);
    public record CorrelationCreated(Guid Id, string Name);
    public record HierCreated(Guid Id, string Name, bool Child);

    public class MappedDoc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Tenant { get; set; } = "";
    }

    public class LastModifiedDoc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public DateTimeOffset LastModified { get; set; }
    }

    public class CorrelationDoc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string CorrelationId { get; set; } = "";
    }

    public class HierBase
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Tenant { get; set; } = "";
    }

    public class HierChild: HierBase
    {
        public bool ChildField { get; set; }
    }

    public partial class LastModifiedProjection: SingleStreamProjection<LastModifiedDoc, Guid>
    {
        public static LastModifiedDoc Create(IEvent<LastModifiedCreated> e) =>
            new() { Id = e.Data.Id, Name = e.Data.Name };
    }

    public partial class HierBaseProjection: SingleStreamProjection<HierBase, Guid>
    {
        public static HierBase Create(IEvent<HierCreated> e) =>
            e.Data.Child
                ? new HierChild { Id = e.Data.Id, Name = e.Data.Name, ChildField = true }
                : new HierBase { Id = e.Data.Id, Name = e.Data.Name };
    }
}
