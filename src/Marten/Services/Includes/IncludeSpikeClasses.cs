using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Linq;
using Marten.Schema;

namespace Marten.Services.Includes
{
    public enum JoinType
    {
        Inner,
        LeftOuter
    }

    public class IncludeJoin<T> where T : class
    {
        private readonly JoinType _joinType;
        private readonly DocumentMapping _mapping;
        private readonly MemberInfo[] _members;
        private readonly Action<T> _callback;

        public IncludeJoin(JoinType joinType, DocumentMapping mapping, MemberInfo[] members, Action<T> callback)
        {
            _joinType = joinType;
            _mapping = mapping;
            _members = members;
            _callback = callback;

            TableAlias = _members.Select(x => x.Name.ToLower()).Join("_");
        }

        public string TableAlias { get; }

        public ISelector<TSearched> WrapSelector<TSearched>(ISelector<TSearched> inner)
        {
            if (_mapping.IsHierarchy())
            {
                return new HierarchicalIncludeSelector<TSearched,T>(TableAlias, _callback, inner, _mapping);
            }

            // Switch on IsHierarchy
            throw new NotImplementedException();
        }

        public string ToJoin(IDocumentMapping searched)
        {
            throw new NotImplementedException();
        }
    }

    public class IncludeSelector<TSearched, TIncluded> : ISelector<TSearched> where TIncluded : class
    {
        private readonly string _tableAlias;
        private readonly Action<TIncluded> _callback;
        private readonly ISelector<TSearched> _inner;
        private int _dataIndex;
        private int _idIndex;

        public IncludeSelector(string tableAlias, Action<TIncluded> callback, ISelector<TSearched> inner)
        {
            _tableAlias = tableAlias;
            _callback = callback;
            _inner = inner;
            
        }

        public TSearched Resolve(DbDataReader reader, IIdentityMap map)
        {
            // Do the include here
            var json = reader.GetString(_dataIndex);
            var id = reader[_idIndex];

            var included = map.Get<TIncluded>(id, json);
            _callback(included);

            return _inner.Resolve(reader, map);
        }

        public string[] SelectFields()
        {
            var innerFields = _inner.SelectFields();
            _dataIndex = innerFields.Length;
            _idIndex = _dataIndex + 1;

            return innerFields.Concat(new [] {$"{_tableAlias}.data as {_tableAlias}_data", $"${_tableAlias}.id as {_tableAlias}_id"}).ToArray();
        }

    }


    public class HierarchicalIncludeSelector<TSearched, TIncluded> : ISelector<TSearched> where TIncluded : class
    {
        private readonly string _tableAlias;
        private readonly Action<TIncluded> _callback;
        private readonly ISelector<TSearched> _inner;
        private readonly DocumentMapping _hierarchy;
        private int _dataIndex;
        private int _idIndex;
        private int _typeIndex;

        public HierarchicalIncludeSelector(string tableAlias, Action<TIncluded> callback, ISelector<TSearched> inner, DocumentMapping hierarchy)
        {
            _tableAlias = tableAlias;
            _callback = callback;
            _inner = inner;
            _hierarchy = hierarchy;
        }

        public TSearched Resolve(DbDataReader reader, IIdentityMap map)
        {
            // Do the include here
            var json = reader.GetString(_dataIndex);
            var id = reader[_idIndex];
            var typeAlias = reader.GetString(_typeIndex);
            var concreteType = _hierarchy.TypeFor(typeAlias);


            var included = map.Get<TIncluded>(id, concreteType, json);
            _callback(included);

            return _inner.Resolve(reader, map);
        }

        public string[] SelectFields()
        {
            var innerFields = _inner.SelectFields();
            _dataIndex = innerFields.Length;
            _idIndex = _dataIndex + 1;
            _typeIndex = _idIndex + 1;

            return innerFields.Concat(new[]
            {
                $"{_tableAlias}.data as {_tableAlias}_data",
                $"{_tableAlias}.id as {_tableAlias}_id",
                $"{_tableAlias}.{DocumentMapping.DocumentTypeColumn} as {_tableAlias}_{DocumentMapping.DocumentTypeColumn}",
            }).ToArray();
        }

    }
}