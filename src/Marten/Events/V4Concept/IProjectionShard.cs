namespace Marten.Events.V4Concept
{
    public interface IProjectionShard
    {
        IProjection Parent { get; }
        string ShardName { get; }

    }
}