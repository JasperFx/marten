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

        //F# objects are immutable so we cannot generate ids for them (which is fine as F# users expect immutability)
        if (!idMemberIsSettable(idMember) || StrongTypedIdGeneration.IsFSharpSingleCaseDiscriminatedUnion(idType))
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

        if (StrongTypedIdGeneration.IsCandidate(idType, out var generation))
        {
            ValueTypes.Fill(generation);
            return generation;
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
