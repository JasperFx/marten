#nullable enable
using System.Collections.Generic;
using Marten.Linq.Members;
using Weasel.Core.Serialization;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

public interface ICollectionAware
{
    bool CanReduceInChildCollection();

    ICollectionAwareFilter BuildFragment(ICollectionMember member, ISerializer serializer);
    bool SupportsContainment();

    void PlaceIntoContainmentFilter(ContainmentWhereFilter filter);

    bool CanBeJsonPathFilter();
    void BuildJsonPathFilter(IPostgresqlCommandBuilder builder, Dictionary<string, object> parameters);

    IEnumerable<DictionaryValueUsage> Values();

}

public interface ICollectionAwareFilter: ISqlFragment
{
    ICollectionMember CollectionMember { get; }
    ISqlFragment MoveUnder(ICollectionMember ancestorCollection);


}
