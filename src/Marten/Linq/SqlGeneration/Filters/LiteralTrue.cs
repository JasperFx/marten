using System;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

[Obsolete("move to using the new ones from Weasel")]
public record LiteralTrue : IReversibleWhereFragment
{
    public void Apply(ICommandBuilder builder)
    {
        builder.Append("TRUE");
    }

    public ISqlFragment Reverse()
    {
        return new LiteralFalse();
    }
}

public record LiteralFalse : IReversibleWhereFragment
{
    public void Apply(ICommandBuilder builder)
    {
        builder.Append("TRUE");
    }

    public ISqlFragment Reverse()
    {
        return new LiteralTrue();
    }
}
