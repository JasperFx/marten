using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;

namespace Marten.Schema.Identity;

internal class RequiredUserSuppliedIdGeneration : IIdGeneration
{
    public IEnumerable<Type> KeyTypes { get; set; } = Type.EmptyTypes;
    public bool RequiresSequences { get; set; }
    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        throw new NotImplementedException();
    }
}

public class StrongTypedIdGeneration : IIdGeneration
{
    private static Type[] _legalRawTypes = [typeof(string), typeof(int), typeof(long), typeof(Guid)];

    private StrongTypedIdGeneration(Type idType, ConstructorInfo ctor)
    {

    }

    private StrongTypedIdGeneration(Type identityType, MethodInfo builder)
    {
    }

    public static bool IsCandidate(Type idType, out StrongTypedIdGeneration? idGeneration)
    {
        if (idType.IsNullable())
        {
            idType = idType.GetGenericArguments().Single();
        }

        idGeneration = default;

        if (!idType.IsPublic && !idType.IsNestedPublic) return false;

        var properties = idType.GetProperties().Where(x => _legalRawTypes.Contains(x.PropertyType)).ToArray();
        if (properties.Length == 1)
        {
            var identityType = properties[0].PropertyType;

            var ctor = idType.GetConstructors().FirstOrDefault(x => x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == identityType);

            if (ctor != null)
            {
                idGeneration = new StrongTypedIdGeneration(identityType, ctor);
                return true;
            }

            var builder = idType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(x => x.ReturnType == idType && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == identityType);

            if (builder != null)
            {
                idGeneration = new StrongTypedIdGeneration(identityType, builder);
                return true;
            }
        }


        return false;
    }

    public IEnumerable<Type> KeyTypes { get; set; } = Type.EmptyTypes;
    public bool RequiresSequences { get; set; }
    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        throw new NotImplementedException();
    }
}
