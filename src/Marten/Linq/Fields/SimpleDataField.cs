using System;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Marten.Util;

namespace Marten.Linq.Fields
{
    /// <summary>
    /// Represents a literal field in a sub query that selects a simple or primitive type
    /// </summary>
    public class SimpleDataField : IField
    {
        public SimpleDataField(Type sourceType)
        {
            FieldType = sourceType;
        }

        public MemberInfo[] Members => new MemberInfo[0];
        public string TypedLocator => "data";
        public string RawLocator => "data";
        public object GetValueForCompiledQueryParameter(Expression valueExpression)
        {
            throw new NotSupportedException();
        }

        public Type FieldType { get; }

        public string JSONBLocator => "data";
        public string LocatorForIncludedDocumentId => throw new NotSupportedException();
        public string LocatorFor(string rootTableAlias)
        {
            throw new NotSupportedException();
        }

        public bool ShouldUseContainmentOperator()
        {
            return false;
        }

        public string SelectorForDuplication(string pgType)
        {
            throw new NotSupportedException();
        }

        public ISqlFragment CreateComparison(string op, ConstantExpression value)
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
    }
}
