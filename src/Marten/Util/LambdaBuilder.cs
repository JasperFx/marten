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

        private static readonly MethodInfo _getEnumStringValue = typeof(Enum).GetMethod(nameof(Enum.GetName), BindingFlags.Static | BindingFlags.Public);
        private static readonly MethodInfo _getEnumIntValue = typeof(Convert).GetMethods(BindingFlags.Static | BindingFlags.Public).Single(mi => mi.Name == nameof(Convert.ToInt32) && mi.GetParameters().Count() == 1 && mi.GetParameters().Single().ParameterType == typeof(object));
        private static readonly Expression _trueConstant = Expression.Constant(true);

        public static Expression ToExpression(EnumStorage enumStorage, MemberInfo[] members, ParameterExpression target)
        {
            // Builds expression to retrieve value including enum conversion and null checks:
            // Simple property/field                 target => target.Property
            // Enum conversion to int                target => Convert.ToInt32(target.EnumProperty)
            // Enum conversion to string             target => Enum.GetName(type, target.EnumProperty)
            // Nested property/field null checks     target => target.Inner != null ? target.Inner.Property : default()

            Expression NullCheck(Expression accessor)
            {
                return accessor.Type.IsValueType && !accessor.Type.IsNullableOfT()
                    ? _trueConstant
                    : Expression.NotEqual(accessor, Expression.Constant(null, accessor.Type));
            }

            Expression AddToNullChecks(Expression nullChecks, Expression accessor)
            {
                var check = NullCheck(accessor);
                return check == _trueConstant
                    ? nullChecks
                    : Expression.AndAlso(nullChecks, check);
            }

            Expression ConvertEnumExpression(Type type, Expression accessor)
            {
                return enumStorage == EnumStorage.AsString
                    ? Expression.Call(_getEnumStringValue, Expression.Constant(type),
                        Expression.Convert(accessor, typeof(object)))
                    : Expression.Call(_getEnumIntValue,
                        Expression.Convert(accessor, typeof(object)));
            }

            // Build accessor and null checks expressions.
            var aggregatedExpressions = members.Aggregate(new
                {
                    Accessor = (Expression) target,
                    NullChecks = NullCheck(target)
                },
                (acc, member) =>
                {
                    var memberType = member.GetMemberType();
                    var accessor = (Expression) Expression.PropertyOrField(acc.Accessor, member.Name);
                    return new
                    {
                        Accessor = memberType.GetTypeInfo().IsEnum
                            ? ConvertEnumExpression(memberType, accessor)
                            : accessor,
                        NullChecks = AddToNullChecks(acc.NullChecks, accessor)
                    };
                });

            // If there are potential nulls add condition.
            return aggregatedExpressions.NullChecks == _trueConstant
                ? aggregatedExpressions.Accessor
                : Expression.Condition(aggregatedExpressions.NullChecks, aggregatedExpressions.Accessor,
                    Expression.Default(aggregatedExpressions.Accessor.Type));
        }
    }
}