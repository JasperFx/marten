using JasperFx.Core;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal class FromFragment: ISqlFragment
{
    private readonly string _text;

    public FromFragment(DbObjectName name, string? alias = null)
    {
        _text = $" from {name.QualifiedName}";
        if (alias.IsNotEmpty())
        {
            _text += $" as {alias} ";
        }
        else
        {
            _text += " ";
        }
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_text);
    }
}
