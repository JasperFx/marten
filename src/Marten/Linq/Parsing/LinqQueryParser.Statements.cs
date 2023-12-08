using System.Linq;
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Parsing;

internal partial class LinqQueryParser
{
    internal StatementQuery BuildStatements()
    {
        if (!_collectionUsages.Any())
        {
            var usage = new CollectionUsage(Session.Options, _provider.SourceType);
            _collectionUsages.Insert(0, usage);
        }

        var top = _collectionUsages[0];

        for (var i = 1; i < _collectionUsages.Count; i++)
        {
            _collectionUsages[i - 1].Inner = _collectionUsages[i];
        }

        var documentStorage = Session.StorageFor(top.ElementType);
        var collection = documentStorage.QueryMembers;

        // In case the single value mode is passed through by the MartenLinqProvider
        if (ValueMode != null)
        {
            _collectionUsages.Last().SingleValueMode = ValueMode;
        }

        var statement = top.BuildTopStatement(Session, collection, documentStorage, _provider.Statistics);
        var selectionStatement = statement.SelectorStatement();

        // Deal with query statistics at the last minute
        if (_provider.Statistics != null)
        {
            selectionStatement.SelectClause = selectionStatement.SelectClause.UseStatistics(_provider.Statistics);
        }

        return new StatementQuery(selectionStatement, selectionStatement.Top());
    }

    internal record struct StatementQuery(SelectorStatement MainSelector, Statement Top);
}
