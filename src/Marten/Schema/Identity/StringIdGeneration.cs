#nullable enable
namespace Marten.Schema.Identity;

/// <summary>
///     Validating identity strategy for user supplied string identities
/// </summary>
public class StringIdGeneration: IIdGeneration
{
    public bool IsNumeric { get; } = false;
}
