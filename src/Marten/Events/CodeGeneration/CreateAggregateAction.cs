namespace Marten.Events.CodeGeneration
{
    public enum CreateAggregateAction
    {
        Initialize,
        Assign,
        NullCoalesce // TODO -- this should be in Lamar itself
    }
}
