using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Marten.Events.CodeGeneration;
using Marten.Linq.Parsing;
using Marten.Schema;
using Marten.Util;

namespace Marten.Events.Aggregation
{
    internal enum AggregationScope
    {
        SingleStream,
        MultiStream
    }

    public interface IAggregateVersioning
    {
        public MemberInfo VersionMember
        {
            get;
        }
    }

    internal class AggregateVersioning<T> : IAggregateVersioning
    {
        private readonly AggregationScope _scope;

        public AggregateVersioning(AggregationScope scope)
        {
            _scope = scope;

            var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var props = typeof(T).GetProperties(bindingFlags)
                .Where(x => x.CanWrite)
                .Where(x => x.PropertyType == typeof(int) || x.PropertyType == typeof(long))
                .OfType<MemberInfo>();

            var fields = typeof(T).GetFields(bindingFlags)
                .Where(x => x.FieldType == typeof(int) || x.FieldType == typeof(long))
                .OfType<MemberInfo>();

            var members = props.Concat(fields);
            // ReSharper disable once PossibleMultipleEnumeration
            VersionMember = members.FirstOrDefault(x => x.HasAttribute<VersionAttribute>());
            // ReSharper disable once PossibleMultipleEnumeration
            VersionMember ??= members.FirstOrDefault(x =>
                x.Name.EqualsIgnoreCase("version") && !x.HasAttribute<MartenIgnoreAttribute>());
        }

        public MemberInfo VersionMember
        {
            get;
            private set;
        }

        public void Override(Expression<Func<T, int>> expression)
        {
            VersionMember = FindMembers.Determine(expression).Single();
        }

        public void Override(Expression<Func<T, long>> expression)
        {
            VersionMember = FindMembers.Determine(expression).Single();
        }
    }
}
