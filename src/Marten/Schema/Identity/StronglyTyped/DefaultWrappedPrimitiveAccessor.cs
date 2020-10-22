using System;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp.RuntimeBinder;

namespace Marten.Schema.Identity.StronglyTyped
{
    /// <summary>
    /// The DefaultWrappedPrimitiveAccessor uses a variety of strategies for implementation:
    ///  - attempted casts
    ///  - reflection over fields or properties
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    /// <typeparam name="TPrimitive"></typeparam>
    internal class DefaultWrappedPrimitiveAccessor<TId, TPrimitive>: IWrappedPrimitiveAccessor<TId, TPrimitive>
    {
        // if it fails once it will probably fail every time so we don't
        // waste time catching exceptions once it fails the first time
        private static bool typeCastMaySucceed = true;

        public TPrimitive GetId(TId obj)
        {
            if (TryCast(obj, out TPrimitive castPrimitive))
            {
                return castPrimitive;
            }

            var idMember = GetIdMember();
            if (idMember != null)
            {
                if (TryGetFieldValue(idMember, obj, out var fieldPrimitiveId))
                {
                    return fieldPrimitiveId;
                }

                if (TryGetPropertyValue(idMember, obj, out var propertyPrimitiveId))
                {
                    return propertyPrimitiveId;
                }

                throw new Exception($"Unable to get underlying id on type {typeof(TId)} from member {idMember.Name}");
            }

            throw new Exception($"Unable to find id on type {typeof(TId)}");
        }

        private static bool TryGetPropertyValue(MemberInfo memberInfo, TId obj, out TPrimitive primitiveId)
        {
            if (memberInfo is PropertyInfo property)
            {
                var idValue = property.GetValue(obj);
                if (idValue is TPrimitive primitiveIdValue)
                {
                    primitiveId = primitiveIdValue;
                    return true;
                }
            }

            primitiveId = default;
            return false;
        }

        private static bool TryGetFieldValue(MemberInfo memberInfo, TId obj, out TPrimitive primitiveId)
        {
            if (memberInfo is FieldInfo field)
            {
                var idValue = field.GetValue(obj);
                if (idValue is TPrimitive primitiveIdValue)
                {
                    primitiveId = primitiveIdValue;
                    return true;
                }
            }

            primitiveId = default;
            return false;
        }

        private static bool TryCast<TFrom, TTo>(TFrom from, out TTo to)
        {
            if (!typeCastMaySucceed)
            {
                to = default;
                return false;
            }
            
            try
            {
                dynamic dynamicFrom = from;
                to = (TTo)dynamicFrom;
                return true;
            }
            catch (InvalidCastException)
            {
                typeCastMaySucceed = false;
            }
            catch (RuntimeBinderException)
            {
                typeCastMaySucceed = false;
            }

            to = default;
            return false;
        }

        private static MemberInfo GetIdMember()
        {
            var idMember = typeof(TId).GetMembers(BindingFlags.GetField | BindingFlags.GetProperty | BindingFlags.Public |
                                                  BindingFlags.NonPublic | BindingFlags.Instance)
                .SingleOrDefault(m => string.Equals("id", m.Name, StringComparison.InvariantCultureIgnoreCase));
            return idMember;
        }

        public TId NewId(TPrimitive primitiveId)
        {
            if (typeof(TId) == typeof(TPrimitive))
            {
                return (TId)(object)primitiveId;
            }

            if (TryCast(primitiveId, out TId castWrapper))
            {
                return castWrapper;
            }

            var constructors = typeof(TId).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (TryActivateUsingSingleParameterConstructor(constructors, primitiveId, out var id1))
            {
                return id1;
            }

            if (TryActivateUsingDefaultConstructor(constructors, primitiveId, out var id2))
            {
                return id2;
            }

            throw new Exception($"Unable to create a new {typeof(TId)}");
        }

        private static bool TryActivateUsingDefaultConstructor(ConstructorInfo[] constructors, TPrimitive primitiveId, out TId id)
        {
            var parameterlessConstructor = constructors.SingleOrDefault(ci => ci.GetParameters().Length == 0);
            if (parameterlessConstructor != null)
            {
                id = Activator.CreateInstance<TId>();
                var idMember = GetIdMember();
                if (idMember == null)
                {
                    throw new Exception($"Unable to find id on type {typeof(TId)}");
                }

                if (idMember is FieldInfo field)
                {
                    field.SetValue(id, primitiveId);
                }
                else if (idMember is PropertyInfo property)
                {
                    property.SetValue(id, primitiveId);
                }
                else
                {
                    throw new Exception($"id member is neither field nor property");
                }

                return true;
            }

            id = default;
            return false;
        }

        private static bool TryActivateUsingSingleParameterConstructor(ConstructorInfo[] constructors, TPrimitive primitiveId, out TId id)
        {
            var singleMatchingParameterConstructor = constructors.SingleOrDefault(ci =>
            {
                var parameters = ci.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(TPrimitive);
            });
            if (singleMatchingParameterConstructor != null)
            {
                id = (TId)Activator.CreateInstance(typeof(TId), primitiveId);
                return true;
            }

            id = default;
            return false;
        }
    }
}
