using System;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Fields;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;

namespace Marten.Linq.Includes;

internal class IncludePlan<T>: IIncludePlan
{
    private readonly Action<T> _callback;
    private readonly IDocumentStorage<T> _storage;

    public IncludePlan(IDocumentStorage<T> storage, IField connectingField, Action<T> callback)
    {
        _storage = storage;
        ConnectingField = connectingField;
        _callback = callback;
    }

    public IField ConnectingField { get; }

    public string LeftJoinExpression =>
        $"LEFT JOIN LATERAL {ConnectingField.LocatorForIncludedDocumentId} WITH ORDINALITY as {ExpressionName}({IdAlias}) ON TRUE";

    public Type DocumentType => typeof(T);

    public int Index
    {
        set
        {
            IdAlias = "id" + (value + 1);
            ExpressionName = "include" + (value + 1);
            TempTableSelector = $"{ConnectingField.LocatorForIncludedDocumentId} as {IdAlias}";
        }
    }

    public bool IsIdCollection()
    {
        return ConnectingField is ArrayField;
    }

    public string ExpressionName { get; private set; }

    public string IdAlias { get; private set; }
    public string TempTableSelector { get; private set; }

    public Statement BuildStatement(string tempTableName, IPagedStatement paging, IMartenSession session)
    {
        return new IncludedDocumentStatement(_storage, this, tempTableName, paging, session);
    }

    public IIncludeReader BuildReader(IMartenSession session)
    {
        var selector = (ISelector<T>)_storage.BuildSelector(session);
        return new IncludeReader<T>(_callback, selector);
    }

    public class IncludedDocumentStatement: SelectorStatement
    {
        public IncludedDocumentStatement(
            IDocumentStorage<T> storage,
            IncludePlan<T> includePlan,
            string tempTableName,
            IPagedStatement paging,
            IMartenSession session): base(storage, storage.Fields)
        {
            var initial = new InTempTableWhereFragment(tempTableName, includePlan.IdAlias, paging,
                includePlan.IsIdCollection());
            Where = storage.FilterDocuments(null, initial, session);
        }

        protected override void configure(CommandBuilder sql)
        {
            base.configure(sql);
            sql.Append(";\n");
        }
    }
}
