using System;
using System.Linq.Expressions;
using System.Reflection;
using LamarCodeGeneration;
using Marten.Exceptions;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Marten.Util;

namespace Marten.Linq.Fields
{
    public class NullableTypeField: IField
    {
        public NullableTypeField(IField innerField)
        {
            InnerField = innerField;

            Members = innerField.Members;
            TypedLocator = innerField.TypedLocator;
            RawLocator = innerField.RawLocator;
            FieldType = innerField.FieldType;
            JSONBLocator = innerField.JSONBLocator;
            LocatorForIncludedDocumentId = innerField.LocatorForIncludedDocumentId;

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

        public ISqlFragment CreateComparison(string op, ConstantExpression value)
        {
            if (value.Value == null)
            {
                switch (op)
                {
                    case "=":
                        // TODO -- make this a static readonly
                        return new WhereFragment($"{RawLocator} is null");

                    case "!=":
                        // TODO -- make this a static readonly
                        return new WhereFragment($"{RawLocator} is not null");

                    default:
                        throw new BadLinqExpressionException($"Can only compare property type {FieldType.FullNameInCode()} by '=' or '!='");
                }
            }

            return InnerField.CreateComparison(op, value);
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
