#nullable enable
using System;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Storage;

namespace Marten.Storage.Identification;

/// <summary>
/// Spike: <see cref="IIdentification{TDoc, TId}"/> for documents whose
/// <typeparamref name="TDoc"/> id member is <see cref="string"/> and
/// whose strategy is externally-assigned keys (no auto-generation).
/// Equivalent to Marten's existing <c>StringIdGeneration</c> /
/// <c>NoOpIdGeneration</c>: callers supply the id; the strategy only
/// reads it back and refuses to generate one.
/// </summary>
/// <remarks>
/// Throws on <see cref="AssignIfMissing"/> when the caller didn't set
/// the id — matches the codegen-emitted behavior for
/// <c>StringIdGeneration.GenerateCode</c>.
/// </remarks>
public sealed class StringIdentification<TDoc>: IIdentification<TDoc, string>
    where TDoc : notnull
{
    private readonly Func<TDoc, string> _getter;

    public StringIdentification(MemberInfo idMember)
    {
        _getter = LambdaBuilder.Getter<TDoc, string>(idMember);
    }

    public string Identity(TDoc document) => _getter(document);

    public string AssignIfMissing(TDoc document, IMartenDatabase database)
    {
        var current = _getter(document);
        if (current.IsNotEmpty())
        {
            return current;
        }

        throw new InvalidOperationException(
            $"{typeof(TDoc).Name} uses externally-assigned string keys but the document's id is null or empty.");
    }
}
