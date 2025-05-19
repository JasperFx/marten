using System;
using System.Collections.Immutable;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;

namespace EventSourcingTests.Examples;

public static class EnableDocumentTrackingInEventProjection
{
    public static void Sample()
    {
        #region sample_using_enable_document_tracking_in_event_projection_registration

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            opts.Projections.Add(
                new TrackedEventProjection(),
                // Register projection to run it asynchronously
                ProjectionLifecycle.Async,
                // enable document tracking using identity map
                asyncOptions => asyncOptions.EnableDocumentTrackingByIdentity = true
            );
        });

        #endregion
    }
}

#region sample_using_enable_document_tracking_in_event_projection

public enum Team
{
    VisitingTeam,
    HomeTeam
}

public record Run(Guid GameId, Team Team, string Player);

public record BaseballGame
{
    public Guid Id { get; init; }
    public int HomeRuns { get; init; }
    public int VisitorRuns { get; init; }

    public int Outs { get; init; }
    public ImmutableHashSet<string> PlayersWithRuns { get; init; }
}

public class TrackedEventProjection: EventProjection
{
    public TrackedEventProjection()
    {
        throw new NotImplementedException("Redo");
        // ProjectAsync<Run>(async (run, ops) =>
        // {
        //     var game = await ops.LoadAsync<BaseballGame>(run.GameId);
        //
        //     var updatedGame = run.Team switch
        //     {
        //         Team.HomeTeam => game with
        //         {
        //             HomeRuns = game.HomeRuns + 1,
        //             PlayersWithRuns = game.PlayersWithRuns.Add(run.Player)
        //         },
        //         Team.VisitingTeam => game with
        //         {
        //             VisitorRuns = game.VisitorRuns + 1,
        //             PlayersWithRuns = game.PlayersWithRuns.Add(run.Player)
        //         },
        //     };
        //
        //     ops.Store(updatedGame);
        // });
    }
}

#endregion
