using System;
using System.Data.Common;
using System.Reflection;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class SingleFieldSelector<T> : BasicSelector, ISelector<T>
    {
        public SingleFieldSelector(IDocumentMapping mapping, MemberInfo[] members)
            : base(mapping.FieldFor(members).SqlLocator)
        {
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            var raw = reader[0];
            return raw == DBNull.Value ? default(T) : (T) raw;
        }
    }
}