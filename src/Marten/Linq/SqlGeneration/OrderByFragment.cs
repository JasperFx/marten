#nullable enable
using System.Collections.Generic;
using System.Linq;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

/// <summary>
///     The <c>order by</c> clause of a statement.
/// </summary>
/// <remarks>
///     <para>
///         #5298. <c>Expressions</c> used to be a <c>List&lt;string&gt;</c> appended straight to the
///         command, which meant an ordering could not carry a parameter — the whole clause was one opaque
///         piece of SQL text. That is why <c>OrderByNgramRank</c> inlines its search term with
///         <c>Replace("'", "''")</c> rather than parameterizing it: there was nowhere to put a parameter.
///     </para>
///     <para>
///         Holding fragments instead lets an ordering that needs a user value bind it properly.
///         <see cref="LiteralOrdering" /> carries the ordinary member and literal cases, which are built
///         from member locators and never contain user input.
///     </para>
///     <para>
///         This is a breaking change to a public member, made deliberately: the alternative was a second
///         parallel list, which leaves two ways to express the same clause and a standing chance of one
///         being written and the other read.
///     </para>
/// </remarks>
public class OrderByFragment: ISqlFragment
{
    public List<ISqlFragment> Expressions { get; } = new();

    /// <summary>
    ///     Add an ordering expression that is already complete SQL and carries no parameters.
    /// </summary>
    public void Add(string expression)
    {
        Expressions.Add(new LiteralOrdering(expression));
    }

    public void Apply(ICommandBuilder builder)
    {
        if (!Expressions.Any())
        {
            return;
        }

        builder.Append(" order by ");
        Expressions[0].Apply(builder);
        for (var i = 1; i < Expressions.Count; i++)
        {
            builder.Append(", ");
            Expressions[i].Apply(builder);
        }
    }
}

/// <summary>
///     An ordering that is a fixed piece of SQL — a member locator, or a literal supplied through
///     <c>OrderBySql()</c>.
/// </summary>
public class LiteralOrdering: ISqlFragment
{
    public LiteralOrdering(string sql)
    {
        Sql = sql;
    }

    public string Sql { get; }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(Sql);
    }

    public override string ToString() => Sql;
}
