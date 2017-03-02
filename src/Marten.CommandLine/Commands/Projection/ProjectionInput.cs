using Oakton;

namespace Marten.CommandLine.Commands.Projection
{
    public class ProjectionInput: MartenInput
    {
        public enum ProjectionKind
        {
            async,
            inline
        };

        [Description("Projections kind: Async or Inline")]
        public ProjectionKind Kind { get; set; } = ProjectionKind.async;
    }
}
