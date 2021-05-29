using System;
using System.Linq.Expressions;
using System.Reflection;
using LamarCodeGeneration;
using Marten.Exceptions;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Fields
{
    public class NullableTypeField: IField
    {
        private readonly string _isNullSql;
        private readonly string _isNotNullSql;

        public NullableTypeField(IField innerField)
        {
            InnerField = innerField;

            Members = innerField.Members;
            TypedLocator = innerField.TypedLocator;
            RawLocator = innerField.RawLocator;
            FieldType = innerField.FieldType;
            JSONBLocator = innerField.JSONBLocator;
            LocatorForIncludedDocumentId = innerField.LocatorForIncludedDocumentId;

            _isNullSql = $"{RawLocator} is null";
            _isNotNullSql = $"{RawLocator} is not null";
        }

        public IField InnerField { get; }

        public MemberInfo[] Members { get; }
        public string TypedLocator { get; }
        public string RawLocator { get; }
        public object GetValueForCompiledQueryParameter(Expression valueExpression)
        {
            return InnerField.GetValueForCompiledQueryParameter(valueExpression);
        }

        public Type FieldType { get; }
        public string JSONBLocator { get; }
        public string LocatorForIncludedDocumentId { get; }
        public string LocatorFor(string rootTableAlias)
        {
            return InnerField.LocatorFor(rootTableAlias);
        }

        public bool ShouldUseContainmentOperator()
        {
            return InnerField.ShouldUseContainmentOperator();
        }

        public string SelectorForDuplication(string pgType)
        {
            return InnerField.SelectorForDuplication(pgType);
        }

        public ISqlFragment CreateComparison(string op, ConstantExpression value, Expression memberExpression)
        {
            if (value.Value == null)
            {
                switch (op)
                {
                    case "=":
                        return new WhereFragment(_isNullSql);

                    case "!=":
                        return new WhereFragment(_isNotNullSql);

                    default:
                        throw new BadLinqExpressionException($"Can only compare property type {FieldType.FullNameInCode()} by '=' or '!='");
                }
            }

            return InnerField.CreateComparison(op, value, memberExpression);
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
