using System;
using System.Linq.Expressions;
using System.Reflection;
using FubuCore.Reflection;
using Marten.Util;

namespace Marten.Schema
{
    public class JsonLocatorField : IField
    {
        public static JsonLocatorField For<T>(Expression<Func<T, object>> expression)
        {
            var property = ReflectionHelper.GetProperty(expression);


            return new JsonLocatorField(property);
        }


        public JsonLocatorField(MemberInfo member)
        {
            MemberName = member.Name;
            Members = new MemberInfo[] {member};

            var memberType = member.GetMemberType();
            if (memberType == typeof (string))
            {
                SqlLocator = $"d.data -> '{member.Name}'";
            }
            else if (memberType.IsEnum)
            {
                SqlLocator = $"(d.data -> '{member.Name}')::int";
            }
            else
            {
                SqlLocator = $"CAST(d.data -> '{member.Name}' as {TypeMappings.PgTypes[memberType]})";
            }


        }

        public MemberInfo[] Members { get; private set; }
        public string MemberName { get; private set; }
        public string SqlLocator { get; private set; }
        public string LateralJoinDeclaration { get; } = null;
    }
}