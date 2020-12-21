using System;
using Oakton;

namespace Marten.CommandLine.Commands.Projection
{
    [Description("Rebuilds all projections of specified kind")]
    public class ProjectionCommand: MartenCommand<ProjectionInput>
    {
        public ProjectionCommand()
        {
            Usage("Rebuilds a specified kind of projections, either async or inline")
                .Arguments(x => x.Kind);
        }

        protected override bool execute(IDocumentStore store, ProjectionInput input)
        {
            throw new NotImplementedException("REDO");
            return true;
        }

    }
}
