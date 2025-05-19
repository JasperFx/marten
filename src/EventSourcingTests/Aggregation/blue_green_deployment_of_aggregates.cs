using System;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class blue_green_deployment_of_aggregates
{
    [Fact]
    public void apply_the_version_suffix_to_table_alias_of_versioned_aggregate()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Projections.Add<Version2>(ProjectionLifecycle.Async);
        });

        var mapping = store.Options.Storage.MappingFor(typeof(MyAggregate));
        mapping.Alias.ShouldBe("myaggregate_2");
    }

    [Fact]
    public void apply_the_version_suffix_to_table_alias_when_version_attribute_is_on_snapshot_itself()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Projections.Add<Version2>(ProjectionLifecycle.Async);
            opts.Projections.Snapshot<OtherAggregate>(SnapshotLifecycle.Async);
        });

        var mapping = store.Options.Storage.MappingFor(typeof(OtherAggregate));
        mapping.Alias.ShouldBe("otheraggregate_3");
    }
}

public class Version2: SingleStreamProjection<MyAggregate, Guid>
{
    public Version2()
    {
        Version = 2;
    }

    public void Apply(MyAggregate aggregate, AEvent e) => aggregate.ACount++;
}

[ProjectionVersion(3)]
public class OtherAggregate: MyAggregate
{
    public void Apply(AEvent e) => ACount++;
}
