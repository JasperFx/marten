#nullable enable
namespace Marten.Schema.Identity;

/// <summary>
///     User-assigned identity strategy
/// </summary>
public class NoOpIdGeneration: IIdGeneration
{
    public bool IsNumeric => false;
}
