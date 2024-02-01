#nullable enable
namespace Marten.Events.Daemon;

/// <summary>
///     Used internally by asynchronous projections.
/// </summary>
// This is public because it's used by the generated code
public interface IShardAgent
{
    ShardName Name { get; }
    ShardExecutionMode Mode { get; }
    void StartRange(EventRange range);
}
