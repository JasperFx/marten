using System;
using JasperFx.Core.Reflection;

namespace Marten.Exceptions;

public class InvalidValueTypeException: MartenException
{
    public InvalidValueTypeException(Type type, string message) : base($"Type {type.FullNameInCode()} cannot be used as a value type by Marten. " + message)
    {
    }
}
