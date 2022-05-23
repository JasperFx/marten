using System;
using Baseline;
using Marten.Events.Aggregation;
using Marten.Events.CodeGeneration;
using Marten.Schema;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class AggregateVersioningTests
{
    [Theory]
    [InlineData(typeof(ConventionalVersionedAggregate), "Version")]
    [InlineData(typeof(ConventionalVersionedAggregate2), "Version")]
    [InlineData(typeof(ConventionalVersionedAggregate3), "Version")]
    [InlineData(typeof(ConventionalVersionedAggregate4), "Version")]
    [InlineData(typeof(ConventionalVersionedAggregate5), null)]
    [InlineData(typeof(ConventionalVersionedAggregate6), "VersionOverride")]
    [InlineData(typeof(ConventionalVersionedAggregate6Field), "VersionOverride")]
    [InlineData(typeof(ConventionalVersionedAggregate7), null)]
    public void find_conventional_property_or_field(Type aggregateType, string expectedMemberName)
    {
        var versioning =
            typeof(AggregateVersioning<>).CloseAndBuildAs<IAggregateVersioning>(
                AggregationScope.SingleStream, aggregateType);

        (versioning.VersionMember?.Name).ShouldBe(expectedMemberName);
    }

    [Fact]
    public void override_version_member_int()
    {
        var versioning = new AggregateVersioning<AggregateWithMultipleCandidates>(AggregationScope.SingleStream);
        versioning.Override(x => x.RealVersion);
        versioning.VersionMember.Name.ShouldBe(nameof(AggregateWithMultipleCandidates.RealVersion));
    }

    [Fact]
    public void override_version_member_long()
    {
        var versioning = new AggregateVersioning<AggregateWithMultipleCandidates>(AggregationScope.SingleStream);
        versioning.Override(x => x.LongVersion);
        versioning.VersionMember.Name.ShouldBe(nameof(AggregateWithMultipleCandidates.LongVersion));
    }
}

public class AggregateWithMultipleCandidates
{
    public int Version { get; set; }
    public int RealVersion { get; set; }
    public long LongVersion { get; set; }
}

public class ConventionalVersionedAggregate
{
    internal int Version;
}

public class ConventionalVersionedAggregate2
{
    public int Version { get; set; }
}

public class ConventionalVersionedAggregate3
{
    public long Version;
}

public class ConventionalVersionedAggregate4
{
    internal long Version { get; set; }
}

public class ConventionalVersionedAggregate5
{
    // Should not catch
    public string Version { get; set; }
}

public class ConventionalVersionedAggregate6
{
    // Should not catch
    public int Version { get; set; }

    [Version]
    public int VersionOverride { get; set; }
}

public class ConventionalVersionedAggregate6Field
{
    // Should not catch
    public int Version { get; set; }

    [Version] public int VersionOverride;
}

public class ConventionalVersionedAggregate7
{
    [MartenIgnore]
    public int Version { get; set; }
}
