using System;
using System.Linq;
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
                SqlLocator = $"d.data ->> '{member.Name}'";
            }
            else
            {
                SqlLocator = $"CAST(d.data ->> '{member.Name}' as {PgType})";
            }


        }

        public JsonLocatorField(MemberInfo[] members) : base(members)
        {
            var locator = "d.data";


            if (members.Length == 1)
            {
                locator += $" ->> '{members.Single().Name}'";
            }
            else
            {
                for (int i = 0; i < members.Length - 1; i++)
                {
                    locator += $" -> '{members[i].Name}'";
                }

                locator += $" ->> '{members.Last().Name}'";

            }



            SqlLocator = MemberType == typeof (string) ? locator : locator.ApplyCastToLocator(MemberType);
        }

        public string SqlLocator { get; }
        public string LateralJoinDeclaration { get; } = null;
    }
}