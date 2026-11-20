using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Metadata;
using Marten.Schema;
using Marten.Schema.Indexing.Unique;
using Marten.Services;
using Marten.Storage;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests;

/// <summary>
///     Trust-gate for <see cref="StoreOptions.RestoreV9Defaults"/>, the single opt-out for the
///     Marten 10.0 default flips. Each flipped default gets a test that sets it to the 10.0 value
///     and asserts the call restores the 9.x value. A node that flips a default adds its test here.
/// </summary>
public class RestoreV9DefaultsTests
{
    [Fact]
    public void restores_extended_progression_tracking_to_the_v9_value()
    {
        var opts = new StoreOptions();
        opts.Events.EnableExtendedProgressionTracking = true;

        opts.RestoreV9Defaults();

        opts.Events.EnableExtendedProgressionTracking.ShouldBeFalse();
    }

    [Fact]
    public void async_projection_errors_park_by_default()
    {
        // marten#6003: park a failing identity, and pause where it can't park
        var errors = new StoreOptions().Projections.Errors;

        errors.ApplyErrors.ShouldBe(ProjectionErrorAction.Park);
        errors.SerializationErrors.ShouldBe(ProjectionErrorAction.Park);
        errors.SkipUnknownEvents.ShouldBeTrue();
    }

    [Fact]
    public void restores_skipping_async_projection_errors_to_the_v9_value()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        opts.Projections.Errors.ApplyErrors.ShouldBe(ProjectionErrorAction.Skip);
        opts.Projections.Errors.SerializationErrors.ShouldBe(ProjectionErrorAction.Skip);
        opts.Projections.Errors.SkipApplyErrors.ShouldBeTrue();
        opts.Projections.Errors.SkipSerializationErrors.ShouldBeTrue();
    }

    [Fact]
    public void rebuild_errors_still_pause_by_default()
    {
        var errors = new StoreOptions().Projections.RebuildErrors;

        errors.ApplyErrors.ShouldBe(ProjectionErrorAction.Pause);
        errors.SerializationErrors.ShouldBe(ProjectionErrorAction.Pause);
    }

    [Fact]
    public void dcb_conflicts_are_scoped_by_event_type_by_default()
    {
        new StoreOptions().Events.DcbConflictScope.ShouldBe(DcbConflictScope.TagValueAndEventType);
    }

    [Fact]
    public void restores_the_dcb_conflict_scope_to_the_v9_value()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        opts.Events.DcbConflictScope.ShouldBe(DcbConflictScope.TagValue);
    }

    [Fact]
    public void the_side_effect_gate_is_on_by_default_in_jasperfx_3()
    {
        new GateProjection().GateSideEffectsBehindPriorVersion.ShouldBeTrue();
    }

    [Fact]
    public void restores_the_side_effect_gate_off_for_registered_projections_when_the_store_is_built()
    {
        var opts = new StoreOptions();
        opts.RestoreV9Defaults();
        var projection = new GateProjection();
        opts.Projections.Add(projection, ProjectionLifecycle.Async);

        projection.GateSideEffectsBehindPriorVersion.ShouldBeTrue(); // not until the store is built

        opts.ApplyConfiguration();

        projection.GateSideEffectsBehindPriorVersion.ShouldBeFalse();
    }

    [Fact]
    public void leaves_the_gate_on_without_the_restore()
    {
        var opts = new StoreOptions();
        var projection = new GateProjection();
        opts.Projections.Add(projection, ProjectionLifecycle.Async);

        opts.ApplyConfiguration();

        projection.GateSideEffectsBehindPriorVersion.ShouldBeTrue();
    }

    [Fact]
    public void does_not_touch_the_v9_defaults_that_restore_v8_defaults_owns()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        opts.DisableNpgsqlLogging.ShouldBeTrue();
        opts.Events.UseIdentityMapForAggregates.ShouldBeTrue();
        opts.Events.EnableBigIntEvents.ShouldBeTrue();
    }

    [Fact]
    public void overrides_made_after_the_call_stick()
    {
        var opts = new StoreOptions();
        opts.RestoreV9Defaults();
        opts.Events.EnableExtendedProgressionTracking = true;

        opts.Events.EnableExtendedProgressionTracking.ShouldBeTrue();
    }

    // #5812 item 1: skipped events are filtered out of projections and subscriptions
    [Fact]
    public void event_skipping_is_on_by_default()
    {
        new StoreOptions().Events.EnableEventSkippingInProjectionsOrSubscriptions.ShouldBeTrue();
    }

    [Fact]
    public void restores_event_skipping_to_the_v9_value()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        opts.Events.EnableEventSkippingInProjectionsOrSubscriptions.ShouldBeFalse();
    }

    // #5812 item 2: every optional event metadata column is written and read
    [Fact]
    public void all_event_metadata_is_on_by_default()
    {
        var metadata = new StoreOptions().Events.MetadataConfig;

        metadata.CausationIdEnabled.ShouldBeTrue();
        metadata.CorrelationIdEnabled.ShouldBeTrue();
        metadata.HeadersEnabled.ShouldBeTrue();
        metadata.UserNameEnabled.ShouldBeTrue();
    }

    [Fact]
    public void restores_event_metadata_to_the_v9_values()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        var metadata = opts.Events.MetadataConfig;
        metadata.CausationIdEnabled.ShouldBeFalse();
        metadata.CorrelationIdEnabled.ShouldBeFalse();
        metadata.HeadersEnabled.ShouldBeFalse();
        metadata.UserNameEnabled.ShouldBeFalse();
    }

    // #5812 item 3: causation id, correlation id, headers and last modified by on every document
    [Fact]
    public void document_metadata_is_on_by_default()
    {
        var metadata = new StoreOptions().Storage.MappingFor(typeof(MetadataDoc)).Metadata;

        metadata.CausationId.Enabled.ShouldBeTrue();
        metadata.CorrelationId.Enabled.ShouldBeTrue();
        metadata.Headers.Enabled.ShouldBeTrue();
        metadata.LastModifiedBy.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void restores_document_metadata_to_the_v9_values()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        var metadata = opts.Storage.MappingFor(typeof(MetadataDoc)).Metadata;
        metadata.CausationId.Enabled.ShouldBeFalse();
        metadata.CorrelationId.Enabled.ShouldBeFalse();
        metadata.Headers.Enabled.ShouldBeFalse();
        metadata.LastModifiedBy.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void document_metadata_configured_before_the_restore_still_wins()
    {
        var opts = new StoreOptions();
        opts.Schema.For<MetadataDoc>().Metadata(m => m.CorrelationId.Enabled = true);
        opts.Policies.ForAllDocuments(m => m.Metadata.Headers.Enabled = true);

        opts.RestoreV9Defaults();

        var metadata = opts.Storage.MappingFor(typeof(MetadataDoc)).Metadata;
        metadata.CorrelationId.Enabled.ShouldBeTrue();
        metadata.Headers.Enabled.ShouldBeTrue();
        metadata.CausationId.Enabled.ShouldBeFalse();
        metadata.LastModifiedBy.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void an_itracked_document_maps_its_members_after_the_restore()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        var metadata = opts.Storage.MappingFor(typeof(TrackedDoc)).Metadata;
        metadata.CausationId.EnabledWithMember().ShouldBeTrue();
        metadata.CorrelationId.EnabledWithMember().ShouldBeTrue();
        metadata.LastModifiedBy.EnabledWithMember().ShouldBeTrue();
    }

    // #5812 item 4: the event append counter and the FetchForWriting histogram
    [Fact]
    public void event_counters_and_fetch_for_writing_metrics_are_on_by_default()
    {
        var telemetry = new StoreOptions().OpenTelemetry;

        telemetry.EnableEventCounters.ShouldBeTrue();
        telemetry.EnableFetchForWritingMetrics.ShouldBeTrue();
    }

    [Fact]
    public void restores_the_metrics_to_the_v9_values()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        opts.OpenTelemetry.EnableEventCounters.ShouldBeFalse();
        opts.OpenTelemetry.EnableFetchForWritingMetrics.ShouldBeFalse();
    }

    [Fact]
    public void event_counters_are_exported_when_the_store_is_built()
    {
        var opts = new StoreOptions();

        opts.ApplyMetricsIfAny();
        opts.ApplyMetricsIfAny();

        opts.OpenTelemetry.Applications.Count.ShouldBe(1);
    }

    [Fact]
    public void no_event_counters_are_exported_after_the_restore()
    {
        var opts = new StoreOptions();
        opts.RestoreV9Defaults();

        opts.ApplyMetricsIfAny();

        opts.OpenTelemetry.Applications.ShouldBeEmpty();
    }

    [Fact]
    public void track_event_counters_after_the_restore_turns_them_back_on()
    {
        var opts = new StoreOptions();
        opts.RestoreV9Defaults();
        opts.OpenTelemetry.TrackEventCounters();
        opts.OpenTelemetry.TrackFetchForWritingMetrics();

        opts.ApplyMetricsIfAny();

        opts.OpenTelemetry.Applications.Count.ShouldBe(1);
        opts.OpenTelemetry.EnableFetchForWritingMetrics.ShouldBeTrue();
    }

    // #5812 item 5: System.Text.Json reads "$type" wherever jsonb put it
    private const string OutOfOrderShape = "{\"Radius\":2,\"$type\":\"circle\"}";

    private static Shape readShape(StoreOptions opts)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(OutOfOrderShape));
        return opts.Serializer().FromJson<Shape>(stream);
    }

    [Fact]
    public void out_of_order_metadata_properties_are_allowed_by_default()
    {
        readShape(new StoreOptions()).ShouldBeOfType<Circle>().Radius.ShouldBe(2);
    }

    [Fact]
    public void restores_strict_metadata_property_order_on_the_default_serializer()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        Should.Throw<NotSupportedException>(() => readShape(opts));
    }

    [Fact]
    public void restores_strict_metadata_property_order_on_a_serializer_configured_after_the_call()
    {
        var opts = new StoreOptions();
        opts.RestoreV9Defaults();

        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        Should.Throw<NotSupportedException>(() => readShape(opts));
    }

    [Fact]
    public void restores_strict_metadata_property_order_on_a_serializer_configured_before_the_call()
    {
        var opts = new StoreOptions();
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        opts.RestoreV9Defaults();

        Should.Throw<NotSupportedException>(() => readShape(opts));
    }

    [Fact]
    public void restores_strict_metadata_property_order_on_a_serializer_passed_in_after_the_call()
    {
        var opts = new StoreOptions();
        opts.RestoreV9Defaults();

        opts.Serializer(new Marten.Services.SystemTextJsonSerializer());

        Should.Throw<NotSupportedException>(() => readShape(opts));
    }

    [Fact]
    public void a_configure_callback_after_the_restore_still_wins()
    {
        var opts = new StoreOptions();
        opts.RestoreV9Defaults();

        opts.UseSystemTextJsonForSerialization(configure: o => o.AllowOutOfOrderMetadataProperties = true);

        readShape(opts).ShouldBeOfType<Circle>();
    }

    // #5812 item 6: computed indexes on conjoined documents lead with tenant_id
    [Fact]
    public void computed_indexes_start_with_tenant_id_on_conjoined_documents_by_default()
    {
        var opts = new StoreOptions();
        opts.Schema.For<MetadataDoc>().MultiTenanted();

        opts.Storage.MappingFor(typeof(MetadataDoc)).StartIndexesByTenantId.ShouldBeTrue();
    }

    [Fact]
    public void computed_indexes_do_not_start_with_tenant_id_on_single_tenanted_documents()
    {
        new StoreOptions().Storage.MappingFor(typeof(MetadataDoc)).StartIndexesByTenantId.ShouldBeFalse();
    }

    [Fact]
    public void restores_start_indexes_by_tenant_id_to_the_v9_value()
    {
        var opts = new StoreOptions();
        opts.Schema.For<MetadataDoc>().MultiTenanted();

        opts.RestoreV9Defaults();

        opts.Storage.MappingFor(typeof(MetadataDoc)).StartIndexesByTenantId.ShouldBeFalse();
    }

    [Fact]
    public void an_explicit_start_indexes_by_tenant_id_survives_the_restore()
    {
        var opts = new StoreOptions();
        opts.Schema.For<MetadataDoc>().MultiTenanted().StartIndexesByTenantId();

        opts.RestoreV9Defaults();

        opts.Storage.MappingFor(typeof(MetadataDoc)).StartIndexesByTenantId.ShouldBeTrue();
    }

    [Fact]
    public void a_per_tenant_unique_index_names_tenant_id_once()
    {
        var opts = new StoreOptions();
        opts.Schema.For<MetadataDoc>().MultiTenanted()
            .UniqueIndex(UniqueIndexType.Computed, "uidx_name", TenancyScope.PerTenant, x => x.Name);

        var index = opts.Storage.MappingFor(typeof(MetadataDoc)).Indexes.OfType<ComputedIndex>().Single();

        index.Columns.ShouldBe(["tenant_id", "(data ->> 'Name')"]);
    }

    [Fact]
    public void a_global_unique_index_on_a_conjoined_document_stays_global()
    {
        var opts = new StoreOptions();
        opts.Schema.For<MetadataDoc>().MultiTenanted()
            .UniqueIndex(UniqueIndexType.Computed, "uidx_name", TenancyScope.Global, x => x.Name)
            .Index(x => x.Age);

        var indexes = opts.Storage.MappingFor(typeof(MetadataDoc)).Indexes.OfType<ComputedIndex>().ToArray();

        indexes.Single(x => x.IsUnique).Columns.ShouldBe(["(data ->> 'Name')"]);
        indexes.Single(x => !x.IsUnique).Columns[0].ShouldBe("tenant_id");
    }

    // #5812 item 8: side effects on inline projections
    [Fact]
    public void side_effects_on_inline_projections_are_on_by_default()
    {
        new StoreOptions().Events.EnableSideEffectsOnInlineProjections.ShouldBeTrue();
    }

    [Fact]
    public void restores_side_effects_on_inline_projections_to_the_v9_value()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        opts.Events.EnableSideEffectsOnInlineProjections.ShouldBeFalse();
    }

    // #5812 item 9: archived-stream partitioning and strict stream identity enforcement
    [Fact]
    public void archived_stream_partitioning_and_strict_identity_are_on_by_default()
    {
        var events = new StoreOptions().Events;

        events.UseArchivedStreamPartitioning.ShouldBeTrue();
        events.EnableStrictStreamIdentityEnforcement.ShouldBeTrue();
    }

    [Fact]
    public void restores_archived_stream_partitioning_and_strict_identity_to_the_v9_values()
    {
        var opts = new StoreOptions();

        opts.RestoreV9Defaults();

        opts.Events.UseArchivedStreamPartitioning.ShouldBeFalse();
        opts.Events.EnableStrictStreamIdentityEnforcement.ShouldBeFalse();
    }

    [Fact]
    public void tenant_partitioned_events_switch_archived_partitioning_and_strict_identity_off()
    {
        var opts = new StoreOptions();
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Connection("Host=localhost;Database=never_opened");
        opts.Events.UseTenantPartitionedEvents = true;

        opts.Events.UseArchivedStreamPartitioning.ShouldBeFalse();
        opts.Events.EnableStrictStreamIdentityEnforcement.ShouldBeFalse();

        // and validation no longer refuses the combination
        opts.Validate();
    }

    [Fact]
    public void asking_for_both_tenant_and_archived_partitioning_is_still_refused()
    {
        var opts = new StoreOptions();
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Connection("Host=localhost;Database=never_opened");
        opts.Events.UseTenantPartitionedEvents = true;
        opts.Events.UseArchivedStreamPartitioning = true;

        Should.Throw<InvalidOperationException>(() => opts.Validate());
    }

    public class MetadataDoc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    public class TrackedDoc: ITracked
    {
        public Guid Id { get; set; }
        public string CorrelationId { get; set; } = "";
        public string CausationId { get; set; } = "";
        public string LastModifiedBy { get; set; } = "";
    }

    [JsonPolymorphic]
    [JsonDerivedType(typeof(Circle), "circle")]
    public abstract class Shape;

    public class Circle: Shape
    {
        public int Radius { get; set; }
    }

    public record GateStarted(System.Guid Id);

    public class GateDoc
    {
        public System.Guid Id { get; set; }
    }

    public partial class GateProjection: SingleStreamProjection<GateDoc, System.Guid>
    {
        public void Apply(GateDoc doc, GateStarted _){}
    }
}
