using System;
using System.Linq;
using System.Reflection;
using Baseline;

namespace Marten.Schema
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class UniqueIndexAttribute : MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            var membersGroupedByIndexName = member.DeclaringType.GetMembers()
                .Where(mi => mi.GetCustomAttributes<UniqueIndexAttribute>().Any())
                .Select(mi => new
                {
                    Member = mi,
                    IndexInformation = mi.GetCustomAttributes<UniqueIndexAttribute>().First()
                })
                .GroupBy(m => m.IndexInformation.IndexName ?? m.Member.Name)
                .Where(mg => mg.Any(m => m.Member == member))
                .Single();

            var indexDefinition = new UniqueIndex(
                mapping,
                membersGroupedByIndexName.SelectMany(m => new[] { m.Member }).ToArray(),
                IndexName)
            {
                Method = IndexMethod
            };

            if (IndexName.IsNotEmpty())
                indexDefinition.IndexName = IndexName;

            indexDefinition.IsUnique = true;

            if (!mapping.Indexes.Any(ind => ind.IndexName == indexDefinition.IndexName))
                mapping.Indexes.Add(indexDefinition);
        }

        /// <summary>
        /// Use to override the Postgresql database column type of this searchable field
        /// </summary>
        public string PgType { get; set; } = null;

        /// <summary>
        /// Specifies the type of index to create
        /// </summary>
        public IndexMethod IndexMethod { get; set; } = IndexMethod.btree;

        /// <summary>
        /// Specify the name of the index explicity
        /// </summary>
        public string IndexName { get; set; } = null;
    }
}