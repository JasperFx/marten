using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

/// <summary>
/// Exactly what it sounds like, represents a little bit
/// of literal SQL within a bigger statement
/// </summary>
/// <param name="Text"></param>
// TODO -- move this to Weasel itself
public record LiteralSql(string Text) : ISqlFragment
{
    public void Apply(CommandBuilder builder)
    {
        builder.Append(Text);
    }

    public bool Contains(string sqlText)
    {
        return Text.Contains(sqlText);
    }
}
