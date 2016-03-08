using System;
using System.Reflection;

namespace Marten.Schema
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ForeignKeyAttribute : MartenAttribute
    {
        private readonly Type _referenceType;

        public ForeignKeyAttribute(Type referenceType)
        {
            _referenceType = referenceType;
        }

        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            mapping.AddForeignKey(member.Name, _referenceType);
        }
    }
}