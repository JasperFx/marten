using System;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Includes;

internal class IncludePlan<T>: IIncludePlan
{
    private readonly Action<T> _callback;
    private readonly IQueryableMember _connectingMember;
    private readonly IDocumentStorage<T> _storage;

    public IncludePlan(IDocumentStorage<T> storage, IQueryableMember connectingMember, Action<T> callback)
    {
        _storage = storage;
        _connectingMember = connectingMember;
        _callback = callback;
    }

    public Type DocumentType => typeof(T);

    public IIncludeReader BuildReader(IMartenSession session)
    {
        var selector = (ISelector<T>)_storage.BuildSelector(session);
        return new IncludeReader<T>(_callback, selector);
    }

    public void AppendStatement(TemporaryTableStatement tempTable, IMartenSession martenSession)
    {
        var selector = new SelectorStatement { SelectClause = _storage };
        var filter = new IdInIncludedDocumentIdentifierFilter(tempTable.ExportName, _connectingMember);

        var wrapped = _storage.FilterDocuments(filter, martenSession);
        selector.Wheres.Add(wrapped);

        tempTable.AddToEnd(selector);
    }
}
