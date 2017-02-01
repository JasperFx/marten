using System;
using System.Linq;
using Marten.Events.Projections.Async;
using Oakton;

namespace Marten.CommandLine.Commands.Projection
{
    [Description("Rebuilds all projections of specified kind")]
    public class ProjectionCommand : MartenCommand<ProjectionInput>
    {
        public ProjectionCommand()
        {
            Usage("Rebuilds a specified kind of projections, either async or inline")
                .Arguments(x => x.Kind);
        }

        protected override bool execute(IDocumentStore store, ProjectionInput input)
        {
            var daemon = GetDaemon(input.Kind, store);
            daemon.RebuildAll().Wait();
            return true;
        }

        private IDaemon GetDaemon(ProjectionInput.ProjectionKind inputKind, IDocumentStore store)
        {
            var logger = GetDaemonLogger();
            switch (inputKind)
            {
                case ProjectionInput.ProjectionKind.async:
                    return store.BuildProjectionDaemon(logger: logger);
                case ProjectionInput.ProjectionKind.inline:
                    return store.BuildProjectionDaemon(projections: store.Schema.Events.InlineProjections.ToArray(),
                        logger: GetDaemonLogger());
                default:
                    throw new ArgumentOutOfRangeException(nameof(inputKind), inputKind, null);
            }
        }

        private IDaemonLogger GetDaemonLogger() => new ConsoleDaemonLogger();
    }
}