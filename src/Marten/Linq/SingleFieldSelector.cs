using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class SingleFieldSelector<T> : ISelector<T>
    {
        private readonly string _locator;

        public SingleFieldSelector(IDocumentMapping mapping, MemberInfo[] members)
        {
            if (members == null || !members.Any())
            {
                throw new ArgumentOutOfRangeException(nameof(members), "No members to select!");
            }

            _locator = mapping.FieldFor(members).SqlLocator;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            var raw = reader[0];
            return raw == DBNull.Value ? default(T) : (T)raw;
        }

        public string[] SelectFields()
        {
            return new [] {_locator};
        }

    }
}