using System;
using System.Linq.Expressions;
using Marten.Internal;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public interface ICollectionMember: IQueryableMember
{
    Type ElementType { get; }

    /// <summary>
    ///     Locator against the parent JSONB data that will "explode" the elements into a record set
    /// </summary>
    string ExplodeLocator { get; }

    string ArrayLocator { get;  }

    Statement BuildSelectManyStatement(CollectionUsage collectionUsage, IMartenSession session,
        SelectorStatement parentStatement, QueryStatistics statistics);

    ISelectClause BuildSelectClauseForExplosion(string fromObject);
    ISqlFragment ParseWhereForAny(Expression body, IReadOnlyStoreOptions options);

    IComparableMember ParseComparableForCount(Expression body);

    ISqlFragment ParseWhereForAll(MethodCallExpression body, IReadOnlyStoreOptions options);

    ISqlFragment ParseWhereForContains(MethodCallExpression body, IReadOnlyStoreOptions options);

    ISqlFragment IsEmpty { get; }
    ISqlFragment NotEmpty { get; }

}
