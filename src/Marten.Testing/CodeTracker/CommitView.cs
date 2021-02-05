using System;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Schema;

namespace Marten.Testing.CodeTracker
{
    public class CommitView
    {
        [Identity]
        public string Sha { get; set; }

        public string Message { get; set; }

        public Guid ProjectId { get; set; }

        public int Additions { get; set; }

        public int Deletions { get; set; }

        public DateTimeOffset Timestamp { get; set; }
    }

    public class CommitViewTransform: EventProjection
    {
        public CommitView Transform(IEvent input, Commit data)
        {
            return new CommitView
            {
                Sha = data.Sha,
                Message = data.Message,
                ProjectId = input.StreamId,
                Additions = data.Additions,
                Deletions = data.Deletions,
                Timestamp = data.Timestamp
            };
        }
    }
}
