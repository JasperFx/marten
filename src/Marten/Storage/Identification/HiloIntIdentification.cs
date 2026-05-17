#nullable enable
using System;
using Marten.Storage;

namespace Marten.Storage.Identification;

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
/// The <c>sequenceKey</c> argument is the document <see cref="Type"/> —
/// matches today's <c>SequenceFor(mapping.DocumentType)</c>.
/// </remarks>
public sealed class HiloIntIdentification<TDoc>: IIdentification<TDoc, int>
    where TDoc : notnull
{
    private readonly Func<TDoc, int> _getter;
    private readonly Action<TDoc, int> _setter;
    private readonly Type _sequenceKey;

    public HiloIntIdentification(Func<TDoc, int> getter, Action<TDoc, int> setter, Type sequenceKey)
    {
        _getter = getter;
        _setter = setter;
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
