#nullable enable
using System;
using JasperFx.Core;
using Marten.Storage;

namespace Marten.Storage.Identification;

/// <summary>
/// Spike: <see cref="IIdentification{TDoc, TId}"/> for documents whose
/// <typeparamref name="TDoc"/> id member is <see cref="Guid"/> and whose
/// strategy is sequential-GUID generation (today: <c>CombGuidIdGeneration</c>).
/// </summary>
/// <remarks>
/// No database round-trip — the new id comes from
/// <see cref="CombGuidIdGeneration.NewGuid"/>. Equivalent to the current
/// <c>SequentialGuidIdGeneration.GenerateCode</c> emit, written as a
/// hand-implemented strategy instead of a codegen template.
/// </remarks>
public sealed class SequentialGuidIdentification<TDoc>: IIdentification<TDoc, Guid>
    where TDoc : notnull
{
    private readonly Func<TDoc, Guid> _getter;
    private readonly Action<TDoc, Guid> _setter;

    public SequentialGuidIdentification(Func<TDoc, Guid> getter, Action<TDoc, Guid> setter)
    {
        _getter = getter;
        _setter = setter;
    }

    public Guid Identity(TDoc document) => _getter(document);

    public Guid AssignIfMissing(TDoc document, IMartenDatabase database)
    {
        var current = _getter(document);
        if (current != Guid.Empty)
        {
            return current;
        }

        var assigned = CombGuidIdGeneration.NewGuid();
        _setter(document, assigned);
        return assigned;
    }
}
