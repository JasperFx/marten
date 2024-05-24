using System;
using System.Linq;
using System.Reflection;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Schema;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class)]
public class FullTextIndexAttribute: MartenAttribute
{
    /// <summary>
    ///     Specify Index type
    /// </summary>
    public string RegConfig = FullTextIndexDefinition.DefaultRegConfig;

    /// <summary>
    ///     Specify the name of the index explicity
    /// </summary>
    public string IndexName { get; set; } = null;

    public override void Modify(DocumentMapping mapping)
    {
        mapping.AddFullTextIndex(RegConfig, index => { index.Name = IndexName; });
    }

    public override void Modify(DocumentMapping mapping, MemberInfo member)
    {
        var membersGroupedByIndexName = member.DeclaringType
            .GetMembers()
            .Where(mi => mi.GetCustomAttributes<FullTextIndexAttribute>().Any())
            .Select(mi => new
            {
                Member = mi, IndexInformation = mi.GetCustomAttributes<FullTextIndexAttribute>().First()
            })
            .GroupBy(m => m.IndexInformation.IndexName ?? m.IndexInformation.RegConfig ?? m.Member.Name)
            .Single(mg => mg.Any(m => m.Member == member));

        mapping.AddFullTextIndex(
            membersGroupedByIndexName.Select(mg => new[] { mg.Member }).ToArray(),
            RegConfig,
            IndexName);
    }
}
