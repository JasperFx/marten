using Marten.Internal;
using Marten.Linq.Fields;
using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration;

internal class AllIdSelectorStatement: IdSelectorStatement
{
    public AllIdSelectorStatement(IMartenSession session, IFieldMapping fields, Statement parent): base(session, fields,
        parent)
    {
    }

    protected override void writeWhereClause(CommandBuilder sql)
    {
        sql.Append(" where ");
        Where = new AllComparisionFilter(Where);
        Where.Apply(sql);
    }
}
