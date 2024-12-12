namespace Marten.Events;

/// <summary>
/// The presence of this event marks a stream as "archived" when it is processed
/// by a single stream projection of any sort
/// </summary>
public record Archived(string Reason);
