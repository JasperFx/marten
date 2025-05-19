using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Projections;

namespace EventSourcingTests.Examples.TeleHealth;

public class BoardViewProjection: MultiStreamProjection<BoardView, Guid>
{
    public BoardViewProjection()
    {
        CustomGrouping((_, events, grouping) =>
        {
            grouping.AddEvents<IEvent<BoardStateEvent>>(e => e.StreamId, events);
            grouping.AddEvents<IBoardEvent>(x => x.BoardId, events);

            return Task.CompletedTask;
        });
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
