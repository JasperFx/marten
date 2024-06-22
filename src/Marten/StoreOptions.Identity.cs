using System;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using CombGuidIdGeneration = Marten.Schema.Identity.CombGuidIdGeneration;

namespace Marten;

public partial class StoreOptions
{
    internal IIdGeneration DetermineIdStrategy(Type documentType, MemberInfo idMember)
    {
        var idType = idMember.GetMemberType();

        if (!idMemberIsSettable(idMember) && !FSharpDiscriminatedUnionIdGeneration.IsFSharpSingleCaseDiscriminatedUnion(idType))
        {
            return new NoOpIdGeneration();
        }

        if (idType == typeof(string))
        {
            return new StringIdGeneration();
        }

        if (idType == typeof(Guid))
        {
            return new CombGuidIdGeneration();
        }

        if (idType == typeof(int) || idType == typeof(long))
        {
            return new HiloIdGeneration(documentType, Advanced.HiloSequenceDefaults);
        }

        if (ValueTypeIdGeneration.IsCandidate(idType, out var valueTypeIdGeneration))
        {
            ValueTypes.Fill(valueTypeIdGeneration);
            return valueTypeIdGeneration;
        }

        if (FSharpDiscriminatedUnionIdGeneration.IsCandidate(idType, out var fSharpDiscriminatedUnionIdGeneration))
        {
            ValueTypes.Fill(fSharpDiscriminatedUnionIdGeneration);
            return fSharpDiscriminatedUnionIdGeneration;
        }

        throw new ArgumentOutOfRangeException(nameof(documentType),
            $"Marten cannot use the type {idType.FullName} as the Id for a persisted document. Use int, long, Guid, or string");
    }

    private bool idMemberIsSettable(MemberInfo idMember)
    {
        if (idMember is FieldInfo f) return f.IsPublic;
        if (idMember is PropertyInfo p) return p.CanWrite && p.SetMethod != null;

        return false;
    }
}
