using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Marten.Util;

namespace Marten.Schema
{
    public abstract class Field
    {
        protected Field(MemberInfo member) : this(new[] { member})
        {

        }

        protected Field(MemberInfo[] members)
        {
            Members = members;
            MemberName = members.Select(x => x.Name).Join("");

            MemberType = members.Last().GetMemberType();

            if (MemberType.IsEnum)
            {
                PgType = "integer";
            }
            else
            {
                PgType = TypeMappings.PgTypes[MemberType];
            }
            
        }

        public Type MemberType { get; }
        public string PgType { get; }

        public MemberInfo[] Members { get; }
        public string MemberName { get; }
    }
}