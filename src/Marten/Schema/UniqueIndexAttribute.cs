using System;
using System.Linq;
using System.Reflection;
using Marten.Schema.Indexing.Unique;
using Weasel.Postgresql.Tables;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Schema;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
public class UniqueIndexAttribute: MartenAttribute
{
    /// <summary>
    ///     Specify Index type
    /// </summary>
    public UniqueIndexType IndexType = UniqueIndexType.Computed;

    /// <summary>
    ///     Specify Tenancy for unique index
    /// </summary>
    public TenancyScope TenancyScope = TenancyScope.Global;

    /// <summary>
    ///     Specifies the index should be created in the background and not block/lock
    /// </summary>
    public bool IsConcurrent { get; set; }

    /// <summary>
    ///     Specifies the type of index to create
    /// </summary>
    public IndexMethod IndexMethod { get; set; } = IndexMethod.btree;

    /// <summary>
    ///     Specify the name of the index explicity
    /// </summary>
    public string IndexName { get; set; } = null;

    public override void Modify(DocumentMapping mapping, MemberInfo member)
    {
        var membersGroupedByIndexName = member.DeclaringType!
            .GetMembers()
            .Where(mi => mi.GetCustomAttributes<UniqueIndexAttribute>().Any())
            .Select(mi => new
            {
                Member = mi, IndexInformation = mi.GetCustomAttributes<UniqueIndexAttribute>().First()
            })
            .GroupBy(m => m.IndexInformation.IndexName ?? m.Member.Name)
            .Single(mg => mg.Any(m => m.Member == member));

        mapping.AddUniqueIndex(
            membersGroupedByIndexName.Select(mg => new[] { mg.Member }).ToArray(),
            IndexType,
            IndexName,
            IndexMethod,
            TenancyScope);
    }
}
