using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;
using NpgsqlTypes;

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

            PgType = TypeMappings.GetPgType(MemberType);
        }

        public Type MemberType { get; }
        public string PgType { get; }

        public MemberInfo[] Members { get; }
        public string MemberName { get; }

        public NpgsqlDbType NpgsqlDbType
        {
            get { return TypeMappings.ToDbType(MemberType); }
        }
    }
}