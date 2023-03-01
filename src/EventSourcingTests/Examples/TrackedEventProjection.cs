using System;
using Marten.Events.Projections;

namespace EventSourcingTests.Examples;

#region sample_using_enable_document_tracking_in_event_projection

public enum Team
{
    VisitingTeam,
    HomeTeam
}

public record Out;

public record Run(Guid GameId, Team Team);

public class BaseballGame
{
    public Guid Id { get; set; }
    public int HomeRuns { get; set; }
    public int VisitorRuns { get; set; }

    public int Outs { get; set; }
}

public class TrackedEventProjection : EventProjection
{
    public TrackedEventProjection()
    {
        EnableDocumentTrackingDuringRebuilds = true;

        ProjectAsync<Run>(async (run, ops) =>
        {
            var game = await ops.LoadAsync<BaseballGame>(run.GameId);
            if (run.Team == Team.HomeTeam)
            {
                game.HomeRuns++;
            }
            else
            {
                game.VisitorRuns++;
            }
        });
    }
}

#endregion
