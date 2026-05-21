using JasperFx.Events;
using Marten;
using Marten.Events;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
///     Trust-gate for the Marten 9.0 default flips and <see cref="StoreOptions.RestoreV8Defaults"/>.
///     Every setting whose default changed between Marten 8 and Marten 9 gets one test asserting
///     the new V9 default, plus one test asserting <c>RestoreV8Defaults()</c> returns it to the V8
///     value. The closing test pins down that <c>RestoreV8Defaults()</c> does <i>not</i> touch
///     settings whose default didn't change — so users who chained their own configuration after
///     the call don't have non-flipped fields stomped.
/// </summary>
public class V9DefaultsAndRestoreV8DefaultsTests
{
    // ---------- V9 defaults ----------

    [Fact]
    public void v9_default_for_events_append_mode_is_quick_with_server_timestamps()
    {
        new StoreOptions().Events.AppendMode.ShouldBe(EventAppendMode.QuickWithServerTimestamps);
    }

    [Fact]
    public void v9_default_for_enable_advanced_async_tracking_is_true()
    {
        new StoreOptions().Events.EnableAdvancedAsyncTracking.ShouldBeTrue();
    }

    [Fact]
    public void v9_default_for_use_identity_map_for_aggregates_is_true()
    {
        new StoreOptions().Events.UseIdentityMapForAggregates.ShouldBeTrue();
    }

    [Fact]
    public void v9_default_for_enable_big_int_events_is_true()
    {
        new StoreOptions().Events.EnableBigIntEvents.ShouldBeTrue();
    }

    [Fact]
    public void v9_default_for_disable_npgsql_logging_is_true()
    {
        new StoreOptions().DisableNpgsqlLogging.ShouldBeTrue();
    }

    // ---------- RestoreV8Defaults reverts every flip ----------

    [Fact]
    public void restore_v8_defaults_reverts_append_mode_to_rich()
    {
        var opts = new StoreOptions();
        opts.RestoreV8Defaults();
        opts.Events.AppendMode.ShouldBe(EventAppendMode.Rich);
    }

    [Fact]
    public void restore_v8_defaults_reverts_enable_advanced_async_tracking_to_false()
    {
        var opts = new StoreOptions();
        opts.RestoreV8Defaults();
        opts.Events.EnableAdvancedAsyncTracking.ShouldBeFalse();
    }

    [Fact]
    public void restore_v8_defaults_reverts_use_identity_map_for_aggregates_to_false()
    {
        var opts = new StoreOptions();
        opts.RestoreV8Defaults();
        opts.Events.UseIdentityMapForAggregates.ShouldBeFalse();
    }

    [Fact]
    public void restore_v8_defaults_reverts_enable_big_int_events_to_false()
    {
        var opts = new StoreOptions();
        opts.RestoreV8Defaults();
        opts.Events.EnableBigIntEvents.ShouldBeFalse();
    }

    [Fact]
    public void restore_v8_defaults_reverts_disable_npgsql_logging_to_false()
    {
        var opts = new StoreOptions();
        opts.RestoreV8Defaults();
        opts.DisableNpgsqlLogging.ShouldBeFalse();
    }

    // ---------- RestoreV8Defaults leaves non-flipped settings alone ----------
    //
    // The greenfield post calls out additional settings that V9 deliberately did *not* flip
    // (UseArchivedStreamPartitioning, UseMandatoryStreamTypeDeclaration). RestoreV8Defaults
    // must not stomp them — a user who calls RestoreV8Defaults then explicitly opts into one
    // of those should not have their explicit choice reverted.

    [Fact]
    public void restore_v8_defaults_does_not_touch_use_archived_stream_partitioning()
    {
        var opts = new StoreOptions();
        opts.Events.UseArchivedStreamPartitioning = true;
        opts.RestoreV8Defaults();
        opts.Events.UseArchivedStreamPartitioning.ShouldBeTrue();
    }

    [Fact]
    public void restore_v8_defaults_does_not_touch_use_mandatory_stream_type_declaration()
    {
        var opts = new StoreOptions();
        opts.Events.UseMandatoryStreamTypeDeclaration = true;
        opts.RestoreV8Defaults();
        opts.Events.UseMandatoryStreamTypeDeclaration.ShouldBeTrue();
    }

    // ---------- Chaining behavior: user overrides after RestoreV8Defaults stick ----------

    [Fact]
    public void user_overrides_after_restore_v8_defaults_win()
    {
        var opts = new StoreOptions();
        opts.RestoreV8Defaults();
        opts.Events.AppendMode = EventAppendMode.Quick;
        opts.DisableNpgsqlLogging = true;

        // The user's explicit per-setting overrides land on top of the V8-default reset.
        opts.Events.AppendMode.ShouldBe(EventAppendMode.Quick);
        opts.DisableNpgsqlLogging.ShouldBeTrue();
    }

    [Fact]
    public void restore_v8_defaults_after_user_overrides_still_resets_flipped_settings()
    {
        // Inverse direction — the user touched some flipped settings explicitly and then
        // calls RestoreV8Defaults at the bottom. The reset still wins (because it ran last).
        var opts = new StoreOptions();
        opts.Events.UseIdentityMapForAggregates = true;
        opts.Events.EnableBigIntEvents = true;
        opts.RestoreV8Defaults();

        opts.Events.UseIdentityMapForAggregates.ShouldBeFalse();
        opts.Events.EnableBigIntEvents.ShouldBeFalse();
    }
}
