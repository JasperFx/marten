#nullable enable
namespace Marten.Schema.Identity;

/// <summary>
///     Comb Guid Id Generation. More info http://www.informit.com/articles/article.aspx?p=25862
/// </summary>
public class SequentialGuidIdGeneration: IIdGeneration
{
    public bool IsNumeric { get; } = false;
}
