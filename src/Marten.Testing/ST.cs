using Marten.Testing.Linq;

namespace Marten.Testing
{
    public enum StorytellerKind
    {
        Matching,
        ControlledQuery,
        Single,
        Selection
    }

    public abstract class StorytellerAttribute : System.Attribute
    {
        public StorytellerKind Kind { get; }

        public StorytellerAttribute(StorytellerKind kind)
        {
            Kind = kind;
        }
    }

    public class SelectionStorytellerAttribute : StorytellerAttribute
    {
        public SelectionStorytellerAttribute() : base(StorytellerKind.Selection)
        {
        }
    }

    public class MatchingStorytellerAttribute : StorytellerAttribute
    {
        public MatchingStorytellerAttribute() : base(StorytellerKind.Matching)
        {
        }
    }

    public class ControlledQueryStorytellerAttribute : StorytellerAttribute
    {
        public ControlledQueryStorytellerAttribute() : base(StorytellerKind.ControlledQuery)
        {
        }
    }

    public class SingleStorytellerAttribute : StorytellerAttribute
    {
        public SingleStorytellerAttribute() : base(StorytellerKind.Single)
        {
        }
    }
}