using System;
using System.Linq;
using System.Reflection;

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

            mapping.AddUniqueIndex(
                membersGroupedByIndexName.Select(mg => new[] { mg.Member }).ToArray(),
                IndexType,
                IndexName,
                IndexMethod);
        }

        /// <summary>
        /// Specifies the index should be created in the background and not block/lock
        /// </summary>
        public bool IsConcurrent { get; set; }

        /// <summary>
        /// Specifies the type of index to create
        /// </summary>
        public IndexMethod IndexMethod { get; set; } = IndexMethod.btree;

        /// <summary>
        /// Specify the name of the index explicity
        /// </summary>
        public string IndexName { get; set; } = null;

        /// <summary>
        /// Specify Index type
        /// </summary>
        public UniqueIndexType IndexType = UniqueIndexType.Computed;
    }
}