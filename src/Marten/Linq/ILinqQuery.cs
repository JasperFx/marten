#nullable enable
using System;
using System.Linq.Expressions;

namespace Marten.Linq;

public interface ILinqQuery
{
    CollectionUsage CurrentUsage { get; }
    CollectionUsage CollectionUsageFor(MethodCallExpression expression);
    CollectionUsage CollectionUsageForArgument(Expression argument);
    CollectionUsage StartNewCollectionUsageFor(MethodCallExpression expression);
    CollectionUsage CollectionUsageFor(Type elementType);
}
