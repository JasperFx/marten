using System;
using System.Linq.Expressions;
using System.Reflection;
using FubuCore.Reflection;
using Marten.Util;

namespace Marten.Schema
{
    public class LateralJoinField : IField
    {
        public static LateralJoinField For<T>(Expression<Func<T, object>> expression)
        {
            var property = ReflectionHelper.GetProperty(expression);


            return new LateralJoinField(property);
        }

        public LateralJoinField(MemberInfo member)
        {
            MemberName = member.Name;
            Members = new MemberInfo[] { member };

            SqlLocator = $"l.\"{member.Name}\"";

            var memberType = member.GetMemberType();

            if (memberType == typeof(string))
            {
                LateralJoinDeclaration = $"\"{member.Name}\" varchar";
            }
            else if (memberType.IsEnum)
            {
                LateralJoinDeclaration = $"\"{member.Name}\" integer";
            }
            else
            {
                var pgType = TypeMappings.PgTypes[memberType];
                LateralJoinDeclaration = $"\"{member.Name}\" {pgType}";
            }
        }

        public MemberInfo[] Members { get; private set; }
        public string MemberName { get; private set; }
        public string SqlLocator { get; private set; }
        public string LateralJoinDeclaration { get; private set; }
    }
}