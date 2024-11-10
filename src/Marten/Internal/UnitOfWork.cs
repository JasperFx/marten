using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Internal.Operations;
using Marten.Services;
using Marten.Storage;
using Weasel.Core.Operations;

namespace Marten.Internal;

internal class UnitOfWork: UnitOfWorkBase
{

    private readonly IMartenSession _parent;

    public UnitOfWork(IMartenSession parent) : base(parent)
    {
        _parent = parent;
    }

    internal UnitOfWork(IEnumerable<IStorageOperation> operations) : base(operations)
    {

    }

    protected override UnitOfWorkBase cloneChangeSet(IEnumerable<IStorageOperation> operations)
    {
        return new UnitOfWork(operations);
    }

    protected override bool shouldSort(out IComparer<IStorageOperation> comparer)
    {
        comparer = null;
        if (_operations.Count <= 1)
        {
            return false;
        }

        var rawTypes = _operations
            .Where(x => x.Role() != OperationRole.Other)
            .Select(x => x.DocumentType)
            .Where(x => x != null)
            .Where(x => x != typeof(StorageFeatures))
            .Distinct().ToArray();

        if (rawTypes.Length <= 1)
        {
            return false;
        }

        var hasRelationship = rawTypes.Any(x => _parent.Options.Storage.GetTypeDependencies(x).Intersect(rawTypes).Any());

        if (!hasRelationship)
        {
            return false;
        }

        var types = rawTypes
            .TopologicalSort(type => _parent.Options.Storage.GetTypeDependencies(type)).ToArray();

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

    private class StorageOperationWithDeletionsComparer: IComparer<IStorageOperation>
    {
        private readonly Type[] _topologicallyOrderedTypes;

        public StorageOperationWithDeletionsComparer(Type[] topologicallyOrderedTypes)
        {
            _topologicallyOrderedTypes = topologicallyOrderedTypes;
        }

        public int Compare(IStorageOperation x, IStorageOperation y)
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

        private int FindIndex(IStorageOperation x)
        {
            // Will loop through up the inheritance chain until reaches the end or the index is found, used
            // to handle inheritance as topologically sorted array may not have the subclasses listed
            var documentType = x.DocumentType;
            var index = 0;

            do
            {
                index = _topologicallyOrderedTypes.IndexOf(documentType);
                documentType = documentType.BaseType;
            } while (index == -1 && documentType != null);

            return index;
        }
    }

    private class StorageOperationByTypeComparer: IComparer<IStorageOperation>
    {
        private readonly Type[] _topologicallyOrderedTypes;

        public StorageOperationByTypeComparer(Type[] topologicallyOrderedTypes)
        {
            _topologicallyOrderedTypes = topologicallyOrderedTypes;
        }

        public int Compare(IStorageOperation x, IStorageOperation y)
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

        private int FindIndex(IStorageOperation x)
        {
            // Will loop through up the inheritance chain until reaches the end or the index is found, used
            // to handle inheritance as topologically sorted array may not have the subclasses listed
            var documentType = x.DocumentType;
            var index = 0;

            do
            {
                index = _topologicallyOrderedTypes.IndexOf(documentType);
                documentType = documentType.BaseType;
            } while (index == -1 && documentType != null);

            return index;
        }
    }
}
