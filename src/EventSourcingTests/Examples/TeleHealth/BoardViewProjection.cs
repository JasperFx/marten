using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;

namespace EventSourcingTests.Examples.TeleHealth;

public class BoardViewProjection: ExperimentalMultiStreamProjection<BoardView, Guid>
{
    protected override ValueTask GroupEvents(IEventGrouping<Guid> grouping, IQuerySession session, List<IEvent> events)
    {
        grouping.AddEventsWithMetadata<BoardStateEvent>(e => e.StreamId, events);
        grouping.AddEvents<IBoardEvent>(x => x.BoardId, events);

        return ValueTask.CompletedTask;
    }

    public BoardView Create(BoardOpened opened)
    {
        return new BoardView { Name = opened.Name, Opened = opened.Opened, Date = opened.Date };
    }

    public void Apply(BoardView view, BoardClosed closed)
    {
        view.Closed = closed.Timestamp;
        view.CloseReason = closed.Reason;
    }

    public void Apply(BoardView view, BoardFinished finished)
    {
        view.Deactivated = finished.Timestamp;
    }
}
