using System;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;

namespace Marten.Linq.Fields
{
    [Obsolete("Replace with new FieldBase")]
    public abstract class Field
    {
        protected Field(EnumStorage enumStorage, MemberInfo member) : this(enumStorage, new[] { member })
        {
        }

        protected Field(EnumStorage enumStorage, MemberInfo[] members)
        {
            Members = members;
            MemberName = members.Select(x => x.Name).Join("");

            FieldType = members.Last().GetMemberType();

            PgType = TypeMappings.GetPgType(FieldType, enumStorage);
            _enumStorage = enumStorage;
        }

        public Type FieldType { get; }
        public string PgType { get; set; } // settable so it can be overidden by users

        public MemberInfo[] Members { get; }
        public string MemberName { get; }

        [Obsolete("Try to eliminate this")]
        protected readonly EnumStorage _enumStorage;
    }
}
