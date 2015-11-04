using System;
using System.Linq.Expressions;
using System.Reflection;
using FubuCore.Reflection;
using Marten.Util;

namespace Marten.Schema
{
    public class LateralJoinField : Field, IField
    {
        public static LateralJoinField For<T>(Expression<Func<T, object>> expression)
        {
            var property = ReflectionHelper.GetProperty(expression);


            return new LateralJoinField(property);
        }

        public LateralJoinField(MemberInfo member) : base(member)
        {
            SqlLocator = $"l.\"{member.Name}\"";
            LateralJoinDeclaration = $"\"{member.Name}\" {PgType}";
        }

        public string SqlLocator { get; private set; }
        public string LateralJoinDeclaration { get; private set; }
    }
}