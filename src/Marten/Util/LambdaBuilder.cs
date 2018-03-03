using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;

namespace Marten.Util
{
    public static class LambdaBuilder
    {
        public static Func<TTarget, TProperty> GetProperty<TTarget, TProperty>(PropertyInfo property)
        {
            var target = Expression.Parameter(property.DeclaringType, "target");
            var method = property.GetGetMethod();

            var callGetMethod = Expression.Call(target, method);

            var lambda = method.ReturnType == typeof(TProperty)
                ? Expression.Lambda<Func<TTarget, TProperty>>(callGetMethod, target)
                : Expression.Lambda<Func<TTarget, TProperty>>(Expression.Convert(callGetMethod, typeof(TProperty)),
                    target);

            return ExpressionCompiler.Compile<Func<TTarget, TProperty>>(lambda);
        }

        public static Action<TTarget, TProperty> SetProperty<TTarget, TProperty>(PropertyInfo property)
        {
            var target = Expression.Parameter(property.DeclaringType, "target");
            var value = Expression.Parameter(property.PropertyType, "value");

            var method = property.SetMethod;

            if (method == null) return null;

            var callSetMethod = Expression.Call(target, method, value);

            var lambda = Expression.Lambda<Action<TTarget, TProperty>>(callSetMethod, target, value);

            return ExpressionCompiler.Compile<Action<TTarget, TProperty>>(lambda);
        }


        public static Func<TTarget, TField> GetField<TTarget, TField>(FieldInfo field)
        {
            var target = Expression.Parameter(typeof(TTarget), "target");

            var fieldAccess = Expression.Field(target, field);

            var lambda = field.FieldType == typeof(TField)
                ? Expression.Lambda<Func<TTarget, TField>>(fieldAccess, target)
                : Expression.Lambda<Func<TTarget, TField>>(Expression.Convert(fieldAccess, typeof(TField)), target);

            return ExpressionCompiler.Compile<Func<TTarget, TField>>(lambda);
        }

        public static Func<TTarget, TMember> Getter<TTarget, TMember>(MemberInfo member)
        {
            return member is PropertyInfo
                ? GetProperty<TTarget, TMember>(member.As<PropertyInfo>())
                : GetField<TTarget, TMember>(member.As<FieldInfo>());
        }


        public static Action<TTarget, TField> SetField<TTarget, TField>(FieldInfo field)
        {
            var target = Expression.Parameter(typeof(TTarget), "target");
            var value = Expression.Parameter(typeof(TField), "value");

            var fieldAccess = Expression.Field(target, field);
            var fieldSetter = Expression.Assign(fieldAccess, value);

            var lambda = Expression.Lambda<Action<TTarget, TField>>(fieldSetter, target, value);

            return ExpressionCompiler.Compile<Action<TTarget, TField>>(lambda);
        }


        public static Action<TTarget, TMember> Setter<TTarget, TMember>(MemberInfo member)
        {
            return member is PropertyInfo
                ? SetProperty<TTarget, TMember>(member.As<PropertyInfo>())
                : SetField<TTarget, TMember>(member.As<FieldInfo>());
        }

        public static Func<TTarget, TValue> Getter<TTarget, TValue>(EnumStorage enumStorage, MemberInfo[] members)
        {
            if (members.Length == 1)
            {
                return Getter<TTarget, TValue>(members.Single());
            }

            var target = Expression.Parameter(typeof(TTarget), "target");

            var body = ToExpression(enumStorage, members, target);

            var lambda = Expression.Lambda<Func<TTarget, TValue>>(body, target);

            return ExpressionCompiler.Compile<Func<TTarget, TValue>>(lambda);
        }



        private static readonly MethodInfo _getName = typeof(Enum).GetMethod(nameof(Enum.GetName), BindingFlags.Static | BindingFlags.Public);

        public static Expression ToExpression(EnumStorage enumStorage, MemberInfo[] members, ParameterExpression target)
        {
            Expression body = target;
            foreach (var member in members)
            {
                if (member is PropertyInfo)
                {
                    var propertyInfo = member.As<PropertyInfo>();
                    var getMethod = propertyInfo.GetGetMethod();

                    body = Expression.Call(body, getMethod);
                }
                else
                {
                    var field = member.As<FieldInfo>();
                    body = Expression.Field(body, field);
                }

                var memberType = members.Last().GetMemberType();
                if (memberType.GetTypeInfo().IsEnum && enumStorage == EnumStorage.AsString)
                {
                    body = Expression.Call(_getName, Expression.Constant(memberType), Expression.Convert(body, typeof(object)));
                }
            }

            return body;
        }
    }
}