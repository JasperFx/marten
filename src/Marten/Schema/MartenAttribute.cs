using System;
using System.Reflection;

namespace Marten.Schema
{
    public abstract class MartenAttribute : Attribute
    {
        public virtual void Modify(DocumentMapping mapping) { }
        public virtual void Modify(DocumentMapping mapping, MemberInfo member) { }
    }
}