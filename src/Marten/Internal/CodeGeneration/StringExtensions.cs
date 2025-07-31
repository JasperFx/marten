using System;
using System.Linq;
using JasperFx.Core;

namespace Marten.Internal.CodeGeneration;

public static class StringExtensions
{
    public static string ToTypeNamePart(this Type type)
    {
        if (type.IsGenericType)
        {
            return type.Name.Split('`').First() + "_of_" +
                   type.GetGenericArguments().Select(x => x.ToTypeNamePart()).Join("_");
        }

        return type.Name;
    }
}
