using System;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace TenantPartitionedEventsTests.Config;

/// <summary>
/// #4596 Phase 0 — surface-level guards on
/// <c>Events.UseTenantPartitionedEvents</c>: the flag defaults to false, and
/// <c>StoreOptions.Validate()</c> rejects the combination with
/// <c>EventAppendMode.Rich</c> (the per-tenant sequence pick lives only in
/// <c>QuickAppendEventFunction</c>). Migrated here from the
/// <c>per_tenant_events_flag</c> nested class in
/// <c>CoreTests/StoreOptionsTests.cs</c> so every per-tenant-partitioning
/// guard lives in one project alongside its companion schema/config tests
/// in <see cref="schema_groundwork_for_partitioned_events"/>.
/// </summary>
public class per_tenant_events_flag_guards
{
    [Fact]
    public void defaults_to_false()
    {
        new StoreOptions().Events.UseTenantPartitionedEvents.ShouldBeFalse();
    }

    [Fact]
    public void validate_throws_when_combined_with_rich_append_mode()
    {
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);
        // #4596 Session 1: per-tenant partitioning also requires Conjoined event
        // tenancy. Set it so the tenancy guard passes and we reach the AppendMode
        // guard we're actually testing.
        options.Events.TenancyStyle = TenancyStyle.Conjoined;
        options.Events.UseTenantPartitionedEvents = true;
        options.Events.AppendMode = EventAppendMode.Rich;

        var ex = Should.Throw<InvalidOperationException>(() => options.Validate());

        ex.Message.ShouldContain(nameof(IEventStoreOptions.UseTenantPartitionedEvents));
        ex.Message.ShouldContain("EventAppendMode.Rich");
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public void validate_succeeds_with_quick_modes(EventAppendMode mode)
    {
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);
        // #4596 Session 1: per-tenant partitioning requires Conjoined event tenancy.
        options.Events.TenancyStyle = TenancyStyle.Conjoined;
        options.Events.UseTenantPartitionedEvents = true;
        options.Events.AppendMode = mode;

        Should.NotThrow(() => options.Validate());
    }
}
