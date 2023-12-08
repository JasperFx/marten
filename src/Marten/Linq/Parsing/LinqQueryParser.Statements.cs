using System.Linq;
using JasperFx.Core;
using Marten.Internal.Storage;
using Marten.Linq.Includes;
using Marten.Linq.Members;
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

        var statement = top.BuildTopStatement(Session, collection, documentStorage);
        var selectionStatement = statement.SelectorStatement();


        parseIncludeExpressions(top, collection);

        if (_provider.AllIncludes.Any())
        {
            var inner = statement.Top();

            if (inner is SelectorStatement { SelectClause: IDocumentStorage storage } select)
            {
                select.SelectClause = storage.SelectClauseWithDuplicatedFields;
            }

            var temp = new TemporaryTableStatement(inner, Session);
            foreach (var include in _provider.AllIncludes) include.AppendStatement(temp, Session);

            temp.AddToEnd(new PassthroughSelectStatement(temp.ExportName, selectionStatement.SelectClause));


            // Deal with query statistics
            if (_provider.Statistics != null)
            {
                selectionStatement.SelectClause = selectionStatement.SelectClause.UseStatistics(_provider.Statistics);
            }

            return new StatementQuery(selectionStatement, temp);
        }


        // Deal with query statistics
        if (_provider.Statistics != null)
        {
            selectionStatement.SelectClause = selectionStatement.SelectClause.UseStatistics(_provider.Statistics);
        }

        return new StatementQuery(selectionStatement, selectionStatement.Top());
    }

    private void parseIncludeExpressions(CollectionUsage top, IQueryableMemberCollection collection)
    {
        if (_hasParsedIncludes)
        {
            return;
        }

        _hasParsedIncludes = true;

        top.ParseIncludes(collection, Session);
        _provider.AllIncludes.AddRange(top.Includes);
    }


    internal record struct StatementQuery(SelectorStatement MainSelector, Statement Top);
}
