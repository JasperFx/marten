using System;
using System.Linq.Expressions;
using System.Reflection;
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
    }
}
