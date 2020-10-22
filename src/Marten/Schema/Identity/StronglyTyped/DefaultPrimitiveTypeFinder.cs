using System;
using System.Linq;
using System.Reflection;

namespace Marten.Schema.Identity.StronglyTyped
{
    internal static class DefaultPrimitiveTypeFinder
    {
        public static Type FindPrimitiveType(Type idType)
        {
            // try to find a single parameter constructor that takes one of our supported primitive types
            // that will probably be the primitive type for the id
            var constructors = idType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            var singleParameterPrimitiveConstructor = constructors
                .Where(ci =>
                {
                    var parameters = ci.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType
                        .IsOneOf(PrimitiveIdTypes.Supported);
                }).ToArray();
            if (singleParameterPrimitiveConstructor.Length == 1)
            {
                return singleParameterPrimitiveConstructor[0].GetParameters()[0].ParameterType;
            }

            // try to find a public property of one of our supported primitive types
            // matching our naming convention
            var publicProperties = idType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(pi => (string.Equals(pi.Name, "id", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(pi.Name, "value",
                                  StringComparison.OrdinalIgnoreCase) // https://github.com/andrewlock/StronglyTypedId
                    ) && pi.PropertyType.IsOneOf(PrimitiveIdTypes.Supported)).ToArray();
            if (publicProperties.Length == 1)
            {
                return publicProperties[0].PropertyType;
            }

            throw new InvalidOperationException($"Unable to determine the underlying primitive type for the id of type {idType.FullName}");
        }
    }
}
