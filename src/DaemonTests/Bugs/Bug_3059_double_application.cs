using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using Marten;
using Marten.Events.Daemon.Coordination;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_3059_double_application
{
    [Fact]
    public async Task work_correctly()
    {
        using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync("bug3059");
            await conn.CloseAsync();
        }

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "bug3059";

                    opts.Projections.LiveStreamAggregation<Incident>();
                    opts.Projections.Snapshot<IncidentDetailsSnapshotAsyncProjection>(SnapshotLifecycle.Async);
                })
                .AddAsyncDaemon(DaemonMode.Solo);
            }).StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();

        var controller = host.Services.GetRequiredService<IProjectionCoordinator>();


        var logIncident = new LogIncident(Guid.NewGuid(), Guid.NewGuid(),
            new Contact(ContactChannel.Email, "Han", "Solo", "han.solo@falcom.com"), "General Solo", Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        using (var session = store.LightweightSession())
        {
            await session.Add<Incident>(logIncident.IncidentId, Handle(logIncident), CancellationToken.None);
        }

        using (var session = store.LightweightSession())
        {
            var command = new CategoriseIncident(logIncident.IncidentId, IncidentCategory.Database, Guid.NewGuid(),
                DateTimeOffset.UtcNow);
            await session.GetAndUpdate<Incident>(logIncident.IncidentId, 1, i => Handle(i, command), CancellationToken.None);
        }


        await controller.DaemonForMainDatabase().WaitForNonStaleData(10.Seconds());

        using (var session = store.LightweightSession())
        {
            var document = await session.LoadAsync<IncidentDetailsSnapshotAsyncProjection>(logIncident.IncidentId);

            document.Aggregated.Category.ShouldBe(IncidentCategory.Database);
        }
    }

    public record LogIncident(
        Guid IncidentId,
        Guid CustomerId,
        Contact Contact,
        string Description,
        Guid LoggedBy,
        DateTimeOffset Now
    );


    public static IncidentLogged Handle(LogIncident command)
    {
        var (incidentId, customerId, contact, description, loggedBy, now) = command;

        return new IncidentLogged(incidentId, customerId, contact, description, loggedBy, now);
    }

    public static IncidentCategorised Handle(Incident current, CategoriseIncident command)
    {
        if (current.Status == IncidentStatus.Closed)
            throw new InvalidOperationException("Incident is already closed");

        var (incidentId, incidentCategory, categorisedBy, now) = command;

        return new IncidentCategorised(incidentId, incidentCategory, categorisedBy, now);
    }
}

public static class DocumentSessionExtensions
{
    public static Task Add<T>(this IDocumentSession documentSession, Guid id, object @event, CancellationToken ct)
        where T : class
    {
        documentSession.Events.StartStream<T>(id, @event);
        return documentSession.SaveChangesAsync(token: ct);
    }

    public static Task GetAndUpdate<T>(
        this IDocumentSession documentSession,
        Guid id,
        int version,
        Func<T, object> handle,
        CancellationToken ct
    ) where T : class =>
        documentSession.Events.WriteToAggregate<T>(id, version, stream =>
            stream.AppendOne(handle(stream.Aggregate)), ct);
}

public record CategoriseIncident(
    Guid IncidentId,
    IncidentCategory Category,
    Guid CategorisedBy,
    DateTimeOffset Now
);


public record CategoriseIncidentRequest(
    IncidentCategory Category
);

public enum IncidentCategory
{
    Software,
    Hardware,
    Network,
    Database
}

public enum IncidentPriority
{
    Critical,
    High,
    Medium,
    Low
}


public enum ResolutionType
{
    Temporary,
    Permanent,
    NotAnIncident
}

public enum ContactChannel
{
    Email,
    Phone,
    InPerson,
    GeneratedBySystem
}

public record Contact(
    ContactChannel ContactChannel,
    string? FirstName = null,
    string? LastName = null,
    string? EmailAddress = null,
    string? PhoneNumber = null
);



public enum IncidentStatus
{
    Pending = 1,
    Resolved = 8,
    ResolutionAcknowledgedByCustomer = 16,
    Closed = 32
}

public record IncidentLogged(
    Guid IncidentId,
    Guid CustomerId,
    Contact Contact,
    string Description,
    Guid LoggedBy,
    DateTimeOffset LoggedAt
);

public record IncidentCategorised(
    Guid IncidentId,
    IncidentCategory Category,
    Guid CategorisedBy,
    DateTimeOffset CategorisedAt
);

public record IncidentPrioritised(
    Guid IncidentId,
    IncidentPriority Priority,
    Guid PrioritisedBy,
    DateTimeOffset PrioritisedAt
);

public record AgentAssignedToIncident(
    Guid IncidentId,
    Guid AgentId,
    DateTimeOffset AssignedAt
);

public record AgentRespondedToIncident(
    Guid IncidentId,
    IncidentResponse.FromAgent Response,
    DateTimeOffset RespondedAt
);

public record CustomerRespondedToIncident(
    Guid IncidentId,
    IncidentResponse.FromCustomer Response,
    DateTimeOffset RespondedAt
);

public record IncidentResolved(
    Guid IncidentId,
    ResolutionType Resolution,
    Guid ResolvedBy,
    DateTimeOffset ResolvedAt
);

public record ResolutionAcknowledgedByCustomer(
    Guid IncidentId,
    Guid AcknowledgedBy,
    DateTimeOffset AcknowledgedAt
);

public record IncidentClosed(
    Guid IncidentId,
    Guid ClosedBy,
    DateTimeOffset ClosedAt
);

public record Incident(
    Guid Id,
    IncidentStatus Status,
    bool HasOutstandingResponseToCustomer = false
)
{
    public static Incident Create(IncidentLogged logged) =>
        new(logged.IncidentId, IncidentStatus.Pending);

    public Incident Apply(AgentRespondedToIncident agentResponded) =>
        this with { HasOutstandingResponseToCustomer = false };

    public Incident Apply(CustomerRespondedToIncident customerResponded) =>
        this with { HasOutstandingResponseToCustomer = true };

    public Incident Apply(IncidentResolved resolved) =>
        this with { Status = IncidentStatus.Resolved };

    public Incident Apply(ResolutionAcknowledgedByCustomer acknowledged) =>
        this with { Status = IncidentStatus.ResolutionAcknowledgedByCustomer };

    public Incident Apply(IncidentClosed closed) =>
        this with { Status = IncidentStatus.Closed };
}

public abstract record IncidentResponse
{
    public record FromAgent(
        Guid AgentId,
        string Content,
        bool VisibleToCustomer
    ) : IncidentResponse(Content);

    public record FromCustomer(
        Guid CustomerId,
        string Content
    ) : IncidentResponse(Content);

    public string Content { get; init; }

    private IncidentResponse(string content)
    {
        Content = content;
    }
}

public record IncidentDetails(
    Guid Id,
    Guid CustomerId,
    IncidentStatus Status,
    IncidentNote[] Notes,
    IncidentCategory? Category = null,
    IncidentPriority? Priority = null,
    Guid? AgentId = null,
    int Version = 1
);

public record IncidentNote(
    IncidentNoteType Type,
    Guid From,
    string Content,
    bool VisibleToCustomer
);

public enum IncidentNoteType
{
    FromAgent,
    FromCustomer
}


public class IncidentDetailsSnapshotAsyncProjection
{
    public Guid Id { get; set; }

    public IncidentDetails Aggregated { get; set; } =
        new(Guid.Empty, Guid.Empty, IncidentStatus.Pending, Array.Empty<IncidentNote>());

    [JsonConstructor]
    public IncidentDetailsSnapshotAsyncProjection()
    {
        // Don't do this.
        //Id = Guid.NewGuid();
    }

    public IncidentDetailsSnapshotAsyncProjection(IncidentLogged logged)
    {
        Id = logged.IncidentId;
        Aggregated = new(logged.IncidentId, logged.CustomerId, IncidentStatus.Pending, Array.Empty<IncidentNote>());
    }

    public void Apply(IncidentCategorised categorised) =>
        Aggregated = Aggregated with { Category = categorised.Category };

    public void Apply(IncidentPrioritised prioritised) =>
        Aggregated = Aggregated with { Priority = prioritised.Priority };

    public void Apply(AgentAssignedToIncident prioritised) =>
        Aggregated = Aggregated with { AgentId = prioritised.AgentId };

    public void Apply(AgentRespondedToIncident agentResponded) =>
        Aggregated = Aggregated with
        {
            Notes = Aggregated.Notes.Union(
                new[]
                {
                    new IncidentNote(
                        IncidentNoteType.FromAgent,
                        agentResponded.Response.AgentId,
                        agentResponded.Response.Content,
                        agentResponded.Response.VisibleToCustomer
                    )
                }).ToArray()
        };

    public void Apply(CustomerRespondedToIncident customerResponded) =>
        Aggregated = Aggregated with
        {
            Notes = Aggregated.Notes.Union(
                new[]
                {
                    new IncidentNote(
                        IncidentNoteType.FromCustomer,
                        customerResponded.Response.CustomerId,
                        customerResponded.Response.Content,
                        true
                    )
                }).ToArray()
        };

    public void Apply(IncidentResolved resolved) =>
        Aggregated = Aggregated with { Status = IncidentStatus.Resolved };

    public void Apply(ResolutionAcknowledgedByCustomer acknowledged) =>
        Aggregated = Aggregated with { Status = IncidentStatus.ResolutionAcknowledgedByCustomer };

    public void Apply(IncidentClosed closed) =>
        Aggregated = Aggregated with { Status = IncidentStatus.Closed };
}

