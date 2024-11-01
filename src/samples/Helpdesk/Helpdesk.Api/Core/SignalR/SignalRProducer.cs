using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Microsoft.AspNetCore.SignalR;

namespace Helpdesk.Api.Core.SignalR;

public class SignalRProducer: IProjection
{
    private readonly IHubContext hubContext;

    public SignalRProducer(IHubContext hubContext) =>
        this.hubContext = hubContext;

    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streamsActions,
        CancellationToken ct)
    {
        foreach (var @event in streamsActions.SelectMany(streamAction => streamAction.Events))
        {
            await hubContext.Clients.All.SendAsync(@event.EventTypeName, @event.Data, ct);
        }
    }

}

