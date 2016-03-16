using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class SingleFieldSelector<T> : ISelector<T>
    {
        private readonly MemberInfo[] _members;

        public SingleFieldSelector(MemberInfo[] members)
        {
            if (members == null || !members.Any())
            {
                throw new ArgumentOutOfRangeException(nameof(members), "No members to select!");
            }

            _members = members;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            var raw = reader[0];
            return raw == DBNull.Value ? default(T) : (T)raw;
        }

        public async Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var raw = await reader.GetFieldValueAsync<object>(0, token).ConfigureAwait(false);
            return raw == DBNull.Value ? default(T) : (T)raw;
        }

        public string SelectClause(IDocumentMapping mapping)
        {
            return mapping.FieldFor(_members).SqlLocator;
        }
    }
}