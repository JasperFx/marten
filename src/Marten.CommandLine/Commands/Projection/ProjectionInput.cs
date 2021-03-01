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
    }
}
