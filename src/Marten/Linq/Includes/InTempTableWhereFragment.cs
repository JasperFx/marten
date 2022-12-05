using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Includes;

internal class InTempTableWhereFragment: ISqlFragment
{
    private readonly bool _isIdCollection;
    private readonly IPagedStatement _paging;
    private readonly string _tempTableColumn;
    private readonly string _tempTableName;

    public InTempTableWhereFragment(string tempTableName, string tempTableColumn, IPagedStatement paging,
        bool isIdCollection)
    {
        _tempTableName = tempTableName;
        _tempTableColumn = tempTableColumn;
        _paging = paging;
        _isIdCollection = isIdCollection;
    }

    public void Apply(CommandBuilder builder)
    {
        builder.Append("id in (select ");
        builder.Append(_isIdCollection ? $"unnest({_tempTableColumn})" : _tempTableColumn);
        builder.Append($" from (select {_tempTableColumn} from ");
        builder.Append(_tempTableName);

        if (_paging.Offset > 0)
        {
            builder.Append(" OFFSET ");
            builder.Append(_paging.Offset);
        }

        if (_paging.Limit > 0)
        {
            builder.Append(" LIMIT ");
            builder.Append(_paging.Limit);
        }

        builder.Append($") as {_tempTableName}");
        builder.Append(")");
    }

    public bool Contains(string sqlText)
    {
        return false;
    }
}
