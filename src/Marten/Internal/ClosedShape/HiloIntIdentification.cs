#nullable enable
using System;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Spike: <see cref="IIdentification{TDoc, TId}"/> for documents whose
/// <typeparamref name="TDoc"/> id member is <see cref="int"/> and whose
/// strategy is Hilo (sequence-backed) generation.
/// </summary>
/// <remarks>
/// New ids come from the per-document-type
/// <see cref="Marten.Schema.Identity.Sequences.ISequence"/> exposed via
/// <see cref="IMartenDatabase.Sequences"/>. Equivalent to the current
/// <c>HiloIdGeneration.GenerateCode</c> emit for the int branch.
/// Accessor delegates are built via <see cref="LambdaBuilder"/> (FEC-compiled).
/// </remarks>
public sealed class HiloIntIdentification<TDoc>: IIdentification<TDoc, int>
    where TDoc : notnull
{
    private readonly Func<TDoc, int> _getter;
    private readonly Action<TDoc, int> _setter;
    private readonly Type _sequenceKey;

    /// <param name="idMember">
    /// The <see cref="PropertyInfo"/> or <see cref="FieldInfo"/> on
    /// <typeparamref name="TDoc"/> that holds the document's id.
    /// </param>
    /// <param name="sequenceKey">
    /// The <see cref="Type"/> used as the cache key for
    /// <see cref="Marten.Schema.Identity.Sequences.ISequences.SequenceFor"/>.
    /// Matches today's <c>SequenceFor(mapping.DocumentType)</c>. Held as a
    /// field rather than recomputed per call.
    /// </param>
    public HiloIntIdentification(MemberInfo idMember, Type sequenceKey)
    {
        _getter = LambdaBuilder.Getter<TDoc, int>(idMember);
        _setter = LambdaBuilder.Setter<TDoc, int>(idMember)!;
        _sequenceKey = sequenceKey;
    }

    public int Identity(TDoc document) => _getter(document);

    public int AssignIfMissing(TDoc document, IMartenDatabase database)
    {
        var current = _getter(document);
        if (current > 0)
        {
            return current;
        }

        var assigned = database.Sequences.SequenceFor(_sequenceKey).NextInt();
        _setter(document, assigned);
        return assigned;
    }
}
