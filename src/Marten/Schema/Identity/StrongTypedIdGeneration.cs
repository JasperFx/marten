using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;

namespace Marten.Schema.Identity;

public class StrongTypedIdGeneration : IIdGeneration
{
    public Type IdentityType { get; }

    private StrongTypedIdGeneration(Type identityType, ConstructorInfo ctor)
    {
        IdentityType = identityType;
    }

    private StrongTypedIdGeneration(Type identityType, MethodInfo builder)
    {
        IdentityType = identityType;
    }

    public static bool IsCandidate(Type idType, out IIdGeneration? idGeneration)
    {
        if (idType.IsNullable())
        {
            idType = idType.GetGenericArguments().Single();
        }

        idGeneration = default;

        if (!idType.IsPublic && !idType.IsNestedPublic) return false;

        var properties = idType.GetProperties().Where(x => DocumentMapping.ValidIdTypes.Contains(x.PropertyType)).ToArray();
        if (properties.Length == 1)
        {
            var identityType = properties[0].PropertyType;
            if (identityType == typeof(string))
            {
                // TODO -- somehow support the aliased name generation that uses HiLo?
                // Custom generation of the inner values???
                idGeneration = new NoOpIdGeneration();
                return true;
            }

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

    public IEnumerable<Type> KeyTypes => Type.EmptyTypes;
    public bool RequiresSequences => false;
    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        throw new NotImplementedException();
    }
}
