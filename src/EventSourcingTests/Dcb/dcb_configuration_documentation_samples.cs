#nullable enable
using System;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;

namespace EventSourcingTests.Dcb;

/// <summary>
///     The store configuration samples in docs/events/dcb.md. Compiled only, never run: the behavior they show is
///     covered by the other tests in this folder.
/// </summary>
public static class dcb_configuration_documentation_samples
{
    public static void automatic_tag_type_registration(StoreOptions opts)
    {
        #region sample_dcb_automatic_tag_type_registration

        // The projection's TId (TicketId) is auto-registered as a tag type
        opts.Projections.Add<TicketSummaryProjection>(ProjectionLifecycle.Inline);

        // No need for: opts.Events.RegisterTagType<TicketId>().ForAggregate<TicketSummary>();

        #endregion
    }

    public static void explicit_registration_wins_over_discovery(StoreOptions opts)
    {
        #region sample_dcb_explicit_tag_type_registration_with_custom_suffix

        // Explicit registration with custom table suffix — auto-discovery won't overwrite this
        opts.Events.RegisterTagType<TicketId>("custom_ticket")
            .ForAggregate<TicketSummary>();
        opts.Projections.Add<TicketSummaryProjection>(ProjectionLifecycle.Inline);

        #endregion
    }

    public static void aot_safe_tag_type_registration(StoreOptions opts)
    {
        #region sample_dcb_aot_safe_tag_type_registration

        // Trim- and AOT-safe: the unwrapper says how to read the stored primitive out of the tag
        opts.Events.RegisterTagType<StudentId, Guid>(id => id.Value, "student")
            .ForAggregate<StudentCourseEnrollment>();

        #endregion
    }

    public static void tag_rules(StoreOptions opts)
    {
        #region sample_dcb_tag_rules

        opts.Events.RegisterTagType<InvoiceId>("invoice");
        opts.Events.RegisterTagType<LineId>("line");

        // Declared on an interface, so it reaches every event that belongs to an invoice
        opts.Events.TagWith<IInvoiceEvent>(e => new InvoiceId(e.Invoice));
        opts.Events.TagWith<LineInvoiced>(e => new LineId(e.Line));

        #endregion
    }

    public static void tag_events_by_one_rule(StoreOptions opts)
    {
        #region sample_dcb_tag_events_by

        opts.Events.TagEventsBy(data => data switch
        {
            LineInvoiced e => [new InvoiceId(e.Invoice), new LineId(e.Line)],
            IInvoiceEvent e => [new InvoiceId(e.Invoice)],
            _ => Array.Empty<object>()
        });

        #endregion
    }

    public static void build_the_hstore_index_concurrently(StoreOptions opts)
    {
        #region sample_dcb_build_hstore_index_concurrently

        opts.Events.DcbStorageMode = DcbStorageMode.HStore;

        // Non-blocking, whichever shape mt_events has
        opts.Events.BuildHStoreTagIndexConcurrently = true;

        #endregion
    }

    public static void own_the_hstore_index_yourself(StoreOptions opts)
    {
        #region sample_dcb_ignore_the_hstore_index

        opts.Events.DcbStorageMode = DcbStorageMode.HStore;

        // Marten adds the tags column, but leaves this index alone
        opts.Events.IgnoreIndex(EventGraph.HStoreTagIndexName);

        #endregion
    }
}
