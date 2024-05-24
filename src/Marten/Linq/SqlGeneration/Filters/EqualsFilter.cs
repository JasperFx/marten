using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

public record EqualsFilter(string Locator, object Value): ISqlFragment
{
    public void Apply(ICommandBuilder builder)
    {
        builder.Append(Locator);
        builder.Append(" = ");
        builder.AppendParameter(Value);
    }

}
