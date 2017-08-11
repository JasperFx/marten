using System;
using System.Reflection;

namespace Marten.Schema
{
    /// <summary>
    /// Direct Marten to make a field or property on a document be
    /// set and tracked as the document version. 
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class VersionAttribute : MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            mapping.VersionMember = member;
        }
    }
}