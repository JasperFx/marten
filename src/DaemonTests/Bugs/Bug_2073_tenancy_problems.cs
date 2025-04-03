using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_2073_tenancy_problems
{
    [Fact]
    public async Task do_not_throw_tenancy_errors()
    {
        var builder = new HostBuilder();
        builder.ConfigureServices(services =>
        {
            services.AddLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Debug);
            });

            services.AddMarten(options =>
            {
                options.Connection(ConnectionSource.ConnectionString);
                options.DatabaseSchemaName = "bug2073";

                // Multi tenancy
                options.Policies.AllDocumentsAreMultiTenanted();
                options.Advanced.DefaultTenantUsageEnabled = false;
                options.Events.TenancyStyle = TenancyStyle.Conjoined;

                options.Events.StreamIdentity = StreamIdentity.AsString;

                // Add projections
                options.Projections.Add<DocumentProjection>(ProjectionLifecycle.Async);
            });
        });

        using var host = await builder.StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        var daemon = (ProjectionDaemon)(await store.BuildProjectionDaemonAsync());
        await daemon.StartAllAsync();

        await using (var session = store.LightweightSession("tenant1"))
        {
            session.Events.Append(Guid.NewGuid().ToString(), new CreateDoc("a", "owner", "content"));
            await session.SaveChangesAsync();
        }

        await daemon.Tracker.WaitForShardState("Document:All", 1);

        daemon.CurrentAgents().Single()
            .Status.ShouldBe(AgentStatus.Running);

        await daemon.StopAllAsync();
    }
}

// events
public record CreateDoc(string DocumentId, string Owner, string Content);

public record ChangeDocOwner(string DocumentId, string OldOwner, string NewOwner);

public record UpdateDoc(string DocumentId, string Content);

// projection
public record Document([property: Identity] string DocumentId, string Owner, string Content);

public class DocumentProjection: SingleStreamProjection<Document, string>
{
    public DocumentProjection()
    {
        ProjectionName = "Document";
    }

    public Document Create(CreateDoc @event)
    {
        return new Document(@event.DocumentId, @event.Owner, @event.Content);
    }

    public Document Apply(UpdateDoc @event, Document current)
    {
        return current with { Content = @event.Content };
    }

    public Document Apply(ChangeDocOwner @event, Document current)
    {
        return current with { Owner = @event.NewOwner };
    }
}
