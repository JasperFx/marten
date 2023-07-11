using System.Collections.Generic;
using System.Linq;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

public class OrderByFragment: ISqlFragment
{
    public List<string> Expressions { get; } = new();

    public void Apply(CommandBuilder builder)
    {
        if (!Expressions.Any())
        {
            return;
        }

        builder.Append(" order by ");
        builder.Append(Expressions[0]);
        for (var i = 1; i < Expressions.Count; i++)
        {
            builder.Append(", ");
            builder.Append(Expressions[i]);
        }
    }

    public bool Contains(string sqlText)
    {
        return Expressions.Any(x => x.Contains(sqlText));
    }
}
