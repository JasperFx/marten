using System;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    public abstract class Field
    {
        protected Field(EnumStorage enumStorage, MemberInfo member) : this(enumStorage, new[] { member })
        {
        }

        protected Field(EnumStorage enumStorage, MemberInfo[] members)
        {
            Members = members;
            MemberName = members.Select(x => x.Name).Join("");

            MemberType = members.Last().GetMemberType();

            PgType = TypeMappings.GetPgType(MemberType, enumStorage);
            _enumStorage = enumStorage;
        }

        public Type MemberType { get; }
        public string PgType { get; set; } // settable so it can be overidden by users

        public MemberInfo[] Members { get; }
        public string MemberName { get; }

        public NpgsqlDbType NpgsqlDbType => TypeMappings.ToDbType(MemberType);

        protected readonly EnumStorage _enumStorage;
    }
}
