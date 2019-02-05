using System;
using System.Linq;
using System.Reflection;

namespace Marten.Schema
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class)]
    public class FullTextIndexAttribute : MartenAttribute
    {
        public override void Modify(DocumentMapping mapping)
        {
            mapping.AddFullTextIndex(regConfig: RegConfig, (index) => { index.IndexName = IndexName; });
        }

        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            var membersGroupedByIndexName = member.DeclaringType.GetMembers()
                .Where(mi => mi.GetCustomAttributes<FullTextIndexAttribute>().Any())
                .Select(mi => new
                {
                    Member = mi,
                    IndexInformation = mi.GetCustomAttributes<FullTextIndexAttribute>().First()
                })
                .GroupBy(m => m.IndexInformation.IndexName ?? m.IndexInformation.RegConfig ?? m.Member.Name)
                .Where(mg => mg.Any(m => m.Member == member))
                .Single();

            mapping.AddFullTextIndex(
                membersGroupedByIndexName.Select(mg => new[] { mg.Member }).ToArray(),
                regConfig: RegConfig,
                indexName: IndexName);
        }

        /// <summary>
        /// Specify the name of the index explicity
        /// </summary>
        public string IndexName { get; set; } = null;

        /// <summary>
        /// Specify Index type
        /// </summary>
        public string RegConfig = FullTextIndex.DefaultRegConfig;
    }
}