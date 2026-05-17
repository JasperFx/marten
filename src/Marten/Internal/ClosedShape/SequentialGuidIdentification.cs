#nullable enable
using System;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

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
    private readonly Action<TDoc, Guid>? _setter;

    /// <param name="idMember">
    /// The <see cref="PropertyInfo"/> or <see cref="FieldInfo"/> on
    /// <typeparamref name="TDoc"/> that holds the document's id. Used to build
    /// FEC-compiled getter + setter delegates via
    /// <see cref="LambdaBuilder"/> — the same mechanism the existing
    /// <c>DocumentStorage&lt;T, TId&gt;</c> uses for its accessor.
    /// </param>
    public SequentialGuidIdentification(MemberInfo idMember)
    {
        _getter = LambdaBuilder.Getter<TDoc, Guid>(idMember);
        _setter = LambdaBuilder.Setter<TDoc, Guid>(idMember);
    }

    public Guid Identity(TDoc document) => _getter(document);

    public Guid AssignIfMissing(TDoc document, IMartenDatabase database)
    {
        var current = _getter(document);
        if (current != Guid.Empty)
        {
            return current;
        }

        // No setter on the id member (Guid Id { get; }) — the caller is
        // managing identity themselves; bail out and let the user's empty
        // value propagate. Matches the codegen path's `if setter != null`
        // emit pattern.
        if (_setter is null)
        {
            return current;
        }

        var assigned = CombGuidIdGeneration.NewGuid();
        _setter(document, assigned);
        return assigned;
    }
}
