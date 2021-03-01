using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Microsoft.Extensions.Logging;
using Oakton;

namespace Marten.CommandLine.Commands.Projection
{
    public class ProjectionInput: MartenInput
    {
        public ProjectionInput()
        {
            LogLevelFlag = LogLevel.Error;
        }

        [Description("Interactively choose the projections to run")]
        public bool InteractiveFlag { get; set; }

        [Description("Trigger a rebuild of the known projections")]
        public bool RebuildFlag { get; set; }

        [Description("If specified, only run or rebuild the named projection")]
        public string ProjectionFlag { get; set; }

        [Description("If specified, just list the registered projections")]
        public bool ListFlag { get; set; }

        public IList<IAsyncProjectionShard> BuildShards(DocumentStore store)
        {
            var projections = store
                .Options
                .Events
                .Projections
                .Projections;

            if (ProjectionFlag.IsEmpty())
            {
                return projections
                    .Where(x => x.Lifecycle == ProjectionLifecycle.Async)
                    .SelectMany(x => x.AsyncProjectionShards(store))
                    .ToList();
            }

            if (ProjectionFlag.Contains(":"))
            {
                return projections
                    .SelectMany(x => x.AsyncProjectionShards(store))
                    .Where(shard => shard.Name.Identity.EqualsIgnoreCase(ProjectionFlag))
                    .ToList();
            }

            var projectionSource = projections
                .FirstOrDefault(x => x.ProjectionName.EqualsIgnoreCase(ProjectionFlag));

            if (projectionSource == null) return new List<IAsyncProjectionShard>();

            return projectionSource
                .AsyncProjectionShards(store)
                .ToList();
        }
    }
}
