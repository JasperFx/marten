#nullable enable
using System;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Spike: <see cref="IIdentification{TDoc, TId}"/> for documents whose
/// <typeparamref name="TDoc"/> id member is <see cref="long"/> and whose
/// strategy is Hilo (sequence-backed) generation. Sibling of
/// <see cref="HiloIntIdentification{TDoc}"/> — differs only in the
/// <see cref="Marten.Schema.Identity.Sequences.ISequence.NextLong"/>
/// call vs <see cref="Marten.Schema.Identity.Sequences.ISequence.NextInt"/>.
/// Accessor delegates are built via <see cref="LambdaBuilder"/> (FEC-compiled).
/// </summary>
public sealed class HiloLongIdentification<TDoc>: IIdentification<TDoc, long>
    where TDoc : notnull
{
    private readonly Func<TDoc, long> _getter;
    private readonly Action<TDoc, long>? _setter;
    private readonly Type _sequenceKey;

    public HiloLongIdentification(MemberInfo idMember, Type sequenceKey)
    {
        _getter = LambdaBuilder.Getter<TDoc, long>(idMember);
        _setter = LambdaBuilder.Setter<TDoc, long>(idMember);
        _sequenceKey = sequenceKey;
    }

    public long Identity(TDoc document) => _getter(document);

    public long AssignIfMissing(TDoc document, IMartenDatabase database)
    {
        var current = _getter(document);
        if (current > 0L)
        {
            return current;
        }

        if (_setter is null) return current;

        var assigned = database.Sequences.SequenceFor(_sequenceKey).NextLong();

        _setter(document, assigned);

        return assigned;
    }
}
