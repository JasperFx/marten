#nullable enable
using System;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M13): <see cref="IIdentification{TDoc, TId}"/> for
/// documents using Marten's <c>IdentityKey</c> strategy — a string id
/// of the form <c>"{alias}/{nextLong}"</c>, where <c>alias</c> is the
/// document mapping's alias and the suffix comes from the per-document
/// HiLo sequence. Mirrors the existing
/// <c>IdentityKeyGeneration.GenerateCode</c> emit.
/// </summary>
public sealed class IdentityKeyIdentification<TDoc>: IIdentification<TDoc, string>
    where TDoc : notnull
{
    private readonly Func<TDoc, string> _getter;
    private readonly Action<TDoc, string> _setter;
    private readonly string _aliasPrefix;
    private readonly Type _sequenceKey;

    /// <param name="idMember">The string-typed id member on <typeparamref name="TDoc"/>.</param>
    /// <param name="mappingAlias">The mapping's alias — used as the key prefix (<c>"{alias}/..."</c>).</param>
    /// <param name="sequenceKey">The <see cref="Type"/> used to look up the HiLo sequence.</param>
    public IdentityKeyIdentification(MemberInfo idMember, string mappingAlias, Type sequenceKey)
    {
        _getter = LambdaBuilder.Getter<TDoc, string>(idMember);
        _setter = LambdaBuilder.Setter<TDoc, string>(idMember)!;
        _aliasPrefix = mappingAlias + "/";
        _sequenceKey = sequenceKey;
    }

    public string Identity(TDoc document) => _getter(document);

    public string AssignIfMissing(TDoc document, IMartenDatabase database)
    {
        var current = _getter(document);
        if (current.IsNotEmpty())
        {
            return current;
        }

        var nextLong = database.Sequences.SequenceFor(_sequenceKey).NextLong();
        var assigned = _aliasPrefix + nextLong;
        _setter(document, assigned);
        return assigned;
    }
}
