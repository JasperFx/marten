using System;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;

namespace Marten.Linq.Fields
{
    public class IdField : IField
    {
        private readonly MemberInfo _idMember;

        public IdField(MemberInfo idMember)
        {
            _idMember = idMember;
        }

        public MemberInfo[] Members => new[] {_idMember};
        public string TypedLocator { get; } = "d.id";
        public string RawLocator { get; } = "d.id";

        public object GetValueForCompiledQueryParameter(Expression valueExpression)
        {
            return valueExpression.Value();
        }

        public Type FieldType => _idMember.GetMemberType();
        public string JSONBLocator { get; } = null;
        public string LocatorForIncludedDocumentId => TypedLocator;

        public string LocatorFor(string rootTableAlias)
        {
            return rootTableAlias + ".id";
        }

        public bool ShouldUseContainmentOperator()
        {
            return false;
        }

        string IField.SelectorForDuplication(string pgType)
        {
            throw new NotSupportedException();
        }

        public ISqlFragment CreateComparison(string op, ConstantExpression value, Expression memberExpression)
        {
            return new ComparisonFilter(this, new CommandParameter(value), op);
        }

        void ISqlFragment.Apply(CommandBuilder builder)
        {
            builder.Append(TypedLocator);
        }

        bool ISqlFragment.Contains(string sqlText)
        {
            return TypedLocator.Contains(sqlText);
        }

        public string ToOrderExpression(Expression expression)
        {
            return TypedLocator;
        }
    }
}
