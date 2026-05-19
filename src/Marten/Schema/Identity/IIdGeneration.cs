#nullable enable
namespace Marten.Schema.Identity;

/// <summary>
///     Identity generation strategy
/// </summary>
public interface IIdGeneration
{
    /// <summary>
    ///     Does this strategy require number sequences
    /// </summary>
    bool IsNumeric { get; }
}
