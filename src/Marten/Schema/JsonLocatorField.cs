using System;
using System.Linq.Expressions;
using System.Reflection;
using FubuCore.Reflection;
using Marten.Util;

namespace Marten.Schema
{
    public class JsonLocatorField : Field, IField
    {
        public static JsonLocatorField For<T>(Expression<Func<T, object>> expression)
        {
            var property = ReflectionHelper.GetProperty(expression);


            return new JsonLocatorField(property);
        }


        public JsonLocatorField(MemberInfo member) : base(member)
        {
            var memberType = member.GetMemberType();
            if (memberType == typeof (string))
            {
                SqlLocator = $"d.data -> '{member.Name}'";
            }
            else
            {
                SqlLocator = $"CAST(d.data -> '{member.Name}' as {PgType})";
            }


        }

        public string SqlLocator { get; }
        public string LateralJoinDeclaration { get; } = null;
    }
}