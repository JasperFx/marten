using System;
using System.Data.Common;
using System.Reflection;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public interface ISelector<T>
    {
        T Resolve(DbDataReader reader, IIdentityMap map);

        string SelectClause(IDocumentMapping mapping);
    }

    public class WholeDocumentSelector<T> : ISelector<T>
    {
        private readonly IResolver<T> _resolver;

        public WholeDocumentSelector(IResolver<T> resolver)
        {
            _resolver = resolver;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            return _resolver.Resolve(reader, map);
        }

        public string SelectClause(IDocumentMapping mapping)
        {
            return mapping.SelectFields("d");
        }
    }

    public class SingleFieldSelector<T> : ISelector<T>
    {
        private readonly MemberInfo[] _members;

        public SingleFieldSelector(MemberInfo[] members)
        {
            _members = members;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            throw new NotImplementedException();
        }

        public string SelectClause(IDocumentMapping mapping)
        {
            throw new NotImplementedException();
        }
    }
}