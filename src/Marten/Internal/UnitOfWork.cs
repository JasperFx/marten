using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Events;
using Marten.Internal.Operations;
using Marten.Services;
using Marten.Storage;

namespace Marten.Internal;

internal class UnitOfWork: ISessionWorkTracker
{
    private readonly List<Weasel.Storage.IStorageOperation> _eventOperations = new();
    private readonly List<Weasel.Storage.IStorageOperation> _operations = new();
    private readonly IMartenSession _parent;

    /// <summary>
    ///     #5730: every document an operation has been queued for in this unit of work, by reference. Lets
    ///     <see cref="Add" /> skip the walk over <see cref="_operations" /> that looks for an earlier
    ///     operation on the same document, which made queueing N documents O(N²). Only ever cleared alongside
    ///     <see cref="_operations" />, never pruned when an operation is ejected or purged, so a stale entry
    ///     costs at most the walk this replaces and a document can never be missing from it.
    /// </summary>
    private HashSet<object?>? _queuedDocuments;

    public UnitOfWork(IMartenSession parent)
    {
        _parent = parent;
    }

    internal UnitOfWork(IEnumerable<Weasel.Storage.IStorageOperation> operations)
    {
        _operations.AddRange(operations);

        foreach (var operation in _operations)
        {
            if (operation is IDocumentStorageOperation o)
            {
                _queuedDocuments ??= new HashSet<object?>(ReferenceEqualityComparer.Instance);
                _queuedDocuments.Add(o.Document);
            }
        }
    }

    public void Reset()
    {
        _operations.Clear();
        _eventOperations.Clear();
        _queuedDocuments?.Clear();
        Streams.Clear();
    }

    public void Add(Weasel.Storage.IStorageOperation operation)
    {
        if (operation is IDocumentStorageOperation o)
        {
            _queuedDocuments ??= new HashSet<object?>(ReferenceEqualityComparer.Instance);

            // #5730: only a document seen before can have an earlier operation to replace
            if (!_queuedDocuments.Add(o.Document))
            {
                removeOperationsFor(o.Document);
            }
        }

        if (operation.DocumentType == typeof(IEvent))
        {
            _eventOperations.Add(operation);
        }
        else
        {
            _operations.Add(operation);
        }
    }

    // Kept out of Add so the lambda's closure is only allocated when there is something to remove
    private void removeOperationsFor(object? document)
    {
        _operations.RemoveAll(x => x is IDocumentStorageOperation op && op.Document == document);
    }

    public IReadOnlyList<Weasel.Storage.IStorageOperation> AllOperations
    {
        get
        {
            if (_eventOperations.Count == 0) return _operations;
            if (_operations.Count == 0) return _eventOperations;

            var combined = new List<Weasel.Storage.IStorageOperation>(_eventOperations.Count + _operations.Count);
            combined.AddRange(_eventOperations);
            combined.AddRange(_operations);
            return combined;
        }
    }

    public void Sort(StoreOptions options)
    {
        if (shouldSort(options, out var comparer))
        {
            // Must use a stable sort to preserve insertion order for operations
            // on the same document type (required for self-referencing foreign keys)
            var sorted = _operations.OrderBy(f => f, comparer).ToList();
            _operations.Clear();
            _operations.AddRange(sorted);
        }
    }


    IEnumerable<IDeletion> IUnitOfWork.Deletions()
    {
        return _operations.OfType<IDeletion>();
    }

    IEnumerable<IDeletion> IUnitOfWork.DeletionsFor<T>()
    {
        return _operations.OfType<IDeletion>().Where(x => x.DocumentType.CanBeCastTo<T>());
    }

    IEnumerable<IDeletion> IUnitOfWork.DeletionsFor(Type documentType)
    {
        return _operations.OfType<IDeletion>().Where(x => x.DocumentType.CanBeCastTo(documentType));
    }

    IEnumerable<object> IUnitOfWork.Updates()
    {
        var fromTrackers = _parent.ChangeTrackers
            .Where(x => x.DetectChanges(_parent, out var _)).Select(x => x.Document);

        return _operations
            .OfType<IDocumentStorageOperation>()
            .Where(x => x.Role() == OperationRole.Update || x.Role() == OperationRole.Upsert)
            .Select(x => x.Document).Union(fromTrackers);
    }

    IEnumerable<object> IUnitOfWork.Inserts()
    {
        return _operations
            .OfType<IDocumentStorageOperation>()
            .Where(x => x.Role() == OperationRole.Insert)
            .Select(x => x.Document);
    }

    IEnumerable<T> IUnitOfWork.UpdatesFor<T>()
    {
        var fromTrackers = _parent.ChangeTrackers
            .Where(x => x.Document.GetType().CanBeCastTo<T>())
            .Where(x => x.DetectChanges(_parent, out var _))
            .Select(x => x.Document).OfType<T>();

        return _operations
            .OfType<IDocumentStorageOperation>()
            .Where(x => x.Role() == OperationRole.Update || x.Role() == OperationRole.Upsert)
            .Select(x => x.Document)
            .OfType<T>()
            .Concat(fromTrackers)
            .Distinct();
    }

    IEnumerable<T> IUnitOfWork.InsertsFor<T>()
    {
        return _operations
            .OfType<IDocumentStorageOperation>()
            .Where(x => x.Role() == OperationRole.Insert)
            .Select(x => x.Document)
            .OfType<T>();
    }

    IEnumerable<T> IUnitOfWork.AllChangedFor<T>()
    {
        var fromTrackers = _parent.ChangeTrackers
            .Where(x => x.Document.GetType().CanBeCastTo<T>())
            .Where(x => x.DetectChanges(_parent, out var _))
            .Select(x => x.Document).OfType<T>();


        return _operations
            .OfType<IDocumentStorageOperation>()
            .Select(x => x.Document)
            .OfType<T>()
            .Concat(fromTrackers)
            .Distinct();
    }

    public List<StreamAction> Streams { get; } = new();

    IList<StreamAction> IUnitOfWork.Streams()
    {
        return Streams;
    }

    // #4821 INC-4: the public IUnitOfWork surface reports the neutral Weasel.Storage operation
    // type — the closed-shape operations moved to Weasel.Storage and no longer implement the
    // Marten-derived interface.
    IEnumerable<Weasel.Storage.IStorageOperation> IUnitOfWork.Operations()
    {
        return _operations;
    }

    IEnumerable<Weasel.Storage.IStorageOperation> IUnitOfWork.OperationsFor<T>()
    {
        return _operations.Where(x => x.DocumentType.CanBeCastTo<T>());
    }

    IEnumerable<Weasel.Storage.IStorageOperation> IUnitOfWork.OperationsFor(Type documentType)
    {
        return _operations.Where(x => x.DocumentType.CanBeCastTo(documentType));
    }

    IEnumerable<object> IChangeSet.Updated => _operations.OfType<IDocumentStorageOperation>()
        .Where(x => x.Role() == OperationRole.Update || x.Role() == OperationRole.Upsert).Select(x => x.Document);

    IEnumerable<object> IChangeSet.Inserted => _operations.OfType<IDocumentStorageOperation>()
        .Where(x => x.Role() == OperationRole.Insert).Select(x => x.Document);

    IEnumerable<IDeletion> IChangeSet.Deleted => _operations.OfType<IDeletion>();

    IEnumerable<IEvent> IChangeSet.GetEvents()
    {
        return Streams.SelectMany(x => x.Events);
    }

    IEnumerable<StreamAction> IChangeSet.GetStreams()
    {
        return Streams;
    }

    public IChangeSet Clone()
    {
        var clone = new UnitOfWork(_operations);
        clone.Streams.AddRange(Streams);

        return clone;
    }

    public void Eject<T>(T document) where T: notnull
    {
        var operations = operationsFor(typeof(T));
        var matching = operations.OfType<IDocumentStorageOperation>().Where(x => ReferenceEquals(document, x.Document))
            .ToArray();

        foreach (var operation in matching) _operations.Remove(operation);
    }

    public void EjectAllOfType(Type type)
    {
        var operations = operationsFor(type);
        var matching = operations.OfType<IDocumentStorageOperation>().ToArray();

        foreach (var operation in matching) _operations.Remove(operation);
    }

    public bool HasOutstandingWork()
    {
        return _operations.Any() || Streams.Any(x => x.Events.Count > 0 || x.AlwaysEnforceConsistency) || _eventOperations.Any();
    }

    public void EjectAll()
    {
        _operations.Clear();
        _eventOperations.Clear();
        _queuedDocuments?.Clear();
        Streams.Clear();
    }

    public void PurgeOperations<T, TId>(TId id) where T : notnull where TId : notnull
    {
        // Match both the codegen-style StorageOperation<T, TId> base (still
        // used by some non-document operations) and the closed-shape
        // operations that expose their id via IIdentifiedOperation<T, TId>.
        _operations.RemoveAll(op =>
            (op is StorageOperation<T, TId> storage && storage.Id.Equals(id))
            || (op is IIdentifiedOperation<T, TId> typed && typed.Id.Equals(id)));
    }

    public bool TryFindStream(string streamKey, string? tenantId, [NotNullWhen(true)]out StreamAction? stream)
    {
        stream = Streams
            .FirstOrDefault(x => x.Key == streamKey && (tenantId == null || x.TenantId == tenantId));

        return stream != null;
    }

    public bool TryFindStream(Guid streamId, string? tenantId, [NotNullWhen(true)]out StreamAction? stream)
    {
        stream = Streams
            .FirstOrDefault(x => x.Id == streamId && (tenantId == null || x.TenantId == tenantId));

        return stream != null;
    }

    private bool shouldSort(StoreOptions options, [NotNullWhen(true)]out IComparer<Weasel.Storage.IStorageOperation>? comparer)
    {
        comparer = null;
        if (_operations.Count <= 1)
        {
            return false;
        }

        // #5730: one pass, and nothing allocated until a second document type turns up. A unit of work
        // touching a single document type -- the common case -- cannot need ordering, so it is answered
        // without the four LINQ iterators, the Distinct set and the array this used to build on every save.
        Type? firstType = null;
        List<Type>? distinctTypes = null;
        foreach (var operation in _operations)
        {
            if (operation.Role() == OperationRole.Other) continue;

            var documentType = operation.DocumentType;
            if (documentType == null || documentType == typeof(StorageFeatures)) continue;

            if (firstType == null)
            {
                firstType = documentType;
            }
            else if (documentType != firstType)
            {
                distinctTypes ??= [firstType];
                if (!distinctTypes.Contains(documentType)) distinctTypes.Add(documentType);
            }
        }

        if (distinctTypes == null) return false;

        var rawTypes = distinctTypes.ToArray();

        var hasRelationship = rawTypes.Any(x => options.Storage.GetTypeDependencies(x).Intersect(rawTypes).Any());

        if (!hasRelationship) return false;

        var types = rawTypes
            .TopologicalSort(type => options.Storage.GetTypeDependencies(type)).ToArray();

        if (_operations.OfType<IDeletion>().Any())
        {
            comparer = new StorageOperationWithDeletionsComparer(types);
        }
        else
        {
            comparer = new StorageOperationByTypeComparer(types);
        }

        return true;
    }

    private IEnumerable<Weasel.Storage.IStorageOperation> operationsFor(Type documentType)
    {
        return _operations.Where(x => x.DocumentType == documentType);
    }

    private class StorageOperationWithDeletionsComparer: IComparer<Weasel.Storage.IStorageOperation>
    {
        private readonly Type[] _topologicallyOrderedTypes;

        public StorageOperationWithDeletionsComparer(Type[] topologicallyOrderedTypes)
        {
            _topologicallyOrderedTypes = topologicallyOrderedTypes;
        }

        public int Compare(Weasel.Storage.IStorageOperation? x, Weasel.Storage.IStorageOperation? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x?.DocumentType == null || y?.DocumentType == null)
            {
                return 0;
            }

            // Maintain order if same document type and same operation
            if (x.DocumentType == y.DocumentType && x.GetType() == y.GetType())
            {
                return 0;
            }

            var xIndex = FindIndex(x);
            var yIndex = FindIndex(y);

            var xIsDelete = x is IDeletion;
            var yIsDelete = y is IDeletion;

            if (xIsDelete != yIsDelete)
            {
                // Arbitrary order if one is a delete but the other is not, because this will force the sorting
                // to try and compare these documents against others and fall in to the below checks.
                return yIsDelete ? 1 : -1;
            }

            if (xIsDelete)
            {
                // Both are deletes, so we need reverse topological order to inserts, updates and upserts
                return yIndex.CompareTo(xIndex);
            }

            // Both are inserts, updates or upserts so topological
            return xIndex.CompareTo(yIndex);
        }

        private int FindIndex(Weasel.Storage.IStorageOperation x)
        {
            // Will loop through up the inheritance chain until reaches the end or the index is found, used
            // to handle inheritance as topologically sorted array may not have the subclasses listed
            var documentType = x.DocumentType;
            var index = 0;

            do
            {
                index = Array.IndexOf(_topologicallyOrderedTypes, documentType);
                documentType = documentType.BaseType;
            } while (index == -1 && documentType != null);

            return index;
        }
    }

    private class StorageOperationByTypeComparer: IComparer<Weasel.Storage.IStorageOperation>
    {
        private readonly Type[] _topologicallyOrderedTypes;

        public StorageOperationByTypeComparer(Type[] topologicallyOrderedTypes)
        {
            _topologicallyOrderedTypes = topologicallyOrderedTypes;
        }

        public int Compare(Weasel.Storage.IStorageOperation? x, Weasel.Storage.IStorageOperation? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x?.DocumentType == null || y?.DocumentType == null)
            {
                return 0;
            }

            if (x.DocumentType == y.DocumentType)
            {
                return 0;
            }

            var xIndex = FindIndex(x);
            var yIndex = FindIndex(y);

            return xIndex.CompareTo(yIndex);
        }

        private int FindIndex(Weasel.Storage.IStorageOperation x)
        {
            // Will loop through up the inheritance chain until reaches the end or the index is found, used
            // to handle inheritance as topologically sorted array may not have the subclasses listed
            var documentType = x.DocumentType;
            var index = 0;

            do
            {
                index = Array.IndexOf(_topologicallyOrderedTypes, documentType);
                documentType = documentType.BaseType;
            } while (index == -1 && documentType != null);

            return index;
        }
    }
}
