using System;
using System.Reflection;

namespace Marten.Schema
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SearchableAttribute : MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            mapping.DuplicateField(member.Name, PgType);
        }

        public string PgType { get; set; } = null;
    }
}