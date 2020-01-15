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

    public class CommitViewTransform: ITransform<Commit, CommitView>
    {
        public CommitView Transform(EventStream stream, Event<Commit> input)
        {
            return new CommitView
            {
                Sha = input.Data.Sha,
                Message = input.Data.Message,
                ProjectId = stream.Id,
                Additions = input.Data.Additions,
                Deletions = input.Data.Deletions,
                Timestamp = input.Data.Timestamp
            };
        }
    }
}
