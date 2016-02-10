using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;

namespace Marten.Schema
{
    public class ModuloLateralJoinField : Field, IField
    {
        public static LateralJoinField For<T>(Expression<Func<T, object>> expression)
        {
            var property = ReflectionHelper.GetProperty(expression);
            return new LateralJoinField(property);
        }

        public string SqlLocator { get; }
        public string LateralJoinDeclaration { get; }
        public ModuloLateralJoinField(MemberInfo member, int moduloValue) : base(member)
        {
            SqlLocator = $"l.\"{member.Name}\" % {moduloValue}";
            LateralJoinDeclaration = $"\"{member.Name}\" {PgType}";
        }
    }
}