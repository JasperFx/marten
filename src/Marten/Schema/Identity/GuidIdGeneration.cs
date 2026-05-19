#nullable enable
namespace Marten.Schema.Identity;

/// <summary>
///     Simple Guid identity generation
/// </summary>
public class GuidIdGeneration: IIdGeneration
{
    public bool IsNumeric { get; } = false;
}
