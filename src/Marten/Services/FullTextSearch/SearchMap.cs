using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Marten.Services.FullTextSearch
{
    public class SearchMap
    {
        public static SearchMap<T> Register<T>(Action<SearchMap<T>> configure)
        {
            return new SearchMap<T>(configure);
        }

        public SearchMap()
        {
            VectorName = "mt_searchable";
        }

        public string VectorName { get; protected set; }

        protected readonly List<MemberInfo> _searchables = new List<MemberInfo>();

        public IEnumerable<string> GetSearchables()
        {
            return _searchables.Select(x => x.Name);
        }
    }

    public sealed class SearchMap<T> : SearchMap
    {
        public SearchMap(Action<SearchMap<T>> configure)
        {
            configure(this);
        }

        public override string ToString()
        {
            return $"Search {typeof(T).Name} over {string.Join(", ", _searchables.Select(x => x.Name))}";
        }

        public SearchMap<T> By<TM>(Expression<Func<T, TM>> configure)
        {
            var fieldMap = MemberInfoFrom(configure);
            _searchables.Add(fieldMap);
            return this;
        }

        public SearchMap<T> UsingVector(string name)
        {
            VectorName = name;
            return this;
        }

        private static MemberInfo MemberInfoFrom<TM>(Expression<Func<T, TM>> memberLambda)
        {
            var memberExpression = memberLambda.Body as MemberExpression;
            if (memberExpression == null)
            {
                throw new InvalidOperationException("Expected property or field access");
            }
            var memberInfo = memberExpression.Member;

            return memberInfo;
        }
    }
}