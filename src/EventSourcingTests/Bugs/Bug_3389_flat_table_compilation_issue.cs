using System;
using Marten.Events.Projections.Flattened;
using Marten.Testing.Harness;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3389_flat_table_compilation_issue : BugIntegrationContext
{
    [Fact]
    public async System.Threading.Tasks.Task try_compilation()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<SiteProjection>(ProjectionLifecycle.Inline);
        });

        var id = theSession.Events.StartStream(new SiteCreated("one")).Id;
        await theSession.SaveChangesAsync();
    }
}

public record SiteCreated(string Name);

public record SiteEnrolledToLite();

public record SiteLocationRecorded(decimal Latitude, decimal Longitude);

public class SiteProjection : FlatTableProjection
{
    public SiteProjection()
        : base("site_projection", SchemaNameSource.DocumentSchema)
    {
        _ = Table.AddColumn<Guid>("id").AsPrimaryKey();

        TeardownDataOnRebuild = true;

        Project<SiteCreated>(map =>
        {
            _ = map.Map(x => x.Name);

            _ = map.SetValue("is_lite", 0);
            _ = map.SetValue("created_at", DateTimeOffset.UtcNow.ToString());
        });

        Project<SiteEnrolledToLite>(map =>
        {
            map.SetValue("is_lite", 0);
        });

        Project<SiteLocationRecorded>(map =>
        {
            _ = map.Map(x => x.Latitude);
            _ = map.Map(x => x.Longitude);
        });
    }
}


