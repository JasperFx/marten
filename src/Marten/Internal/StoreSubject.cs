using System;
using System.Linq;
using System.Text;

namespace Marten.Internal;

/// <summary>
///     Builds the <c>marten://</c> uri that identifies a store — <c>IEventStore.Subject</c>
///     (#5409).
/// </summary>
/// <remarks>
///     <para>
///         <b>Subject identifies the STORE, not the database behind it.</b> Consumers key per-store
///         state by it: CritterWatch resolves every explorer read through <c>store.Subject.ToString()</c>
///         and builds its shard progression id from
///         <c>(serviceName, storeUri, databaseIdentifier, tenantId, shardName)</c>, so two stores
///         answering one subject share a progression id.
///     </para>
///     <para>
///         The primary store's subject used to be the literal <c>marten://main</c>, assigned by a
///         property initializer and overwritten only for an ancillary store — so naming a primary store
///         moved its <c>EventStoreIdentity</c> and left its subject behind, and the two
///         disagreed. They are built from <c>StoreOptions.StoreName</c> together now.
///     </para>
///     <para>
///         <b>Sanitizing is not decoration.</b> A store name is user-supplied text and a uri host is
///         not: verified against .NET 10, <c>new Uri("marten://my store")</c> throws
///         <see cref="UriFormatException" /> and <c>new Uri("marten://a/b")</c> silently parses the
///         tail as a PATH, giving two differently-named stores a chance to collide on host. Neither is
///         an acceptable outcome for a property a user types, so every name goes through
///         <see cref="Sanitize(string)" /> — which is also what #5039's generic-marker fix needed for
///         backticks, one caller over.
///     </para>
/// </remarks>
internal static class StoreSubject
{
    internal const string Scheme = "marten";

    /// <summary>The subject uri for a store called <paramref name="storeName" />.</summary>
    internal static Uri For(string storeName) => new($"{Scheme}://{Sanitize(storeName)}");

    /// <summary>
    ///     Fold a store name down to something that is unambiguously a uri host: lower case, and every
    ///     character a host cannot carry replaced by '-'.
    /// </summary>
    /// <remarks>
    ///     Letters, digits, '-', '.' and '_' survive, so an ordinary store name is unchanged and this
    ///     agrees with Polecat's <c>polecat://{storename}</c> and Fisher's <c>fisher://{storename}</c>
    ///     for every name anybody actually writes. An empty or all-punctuation name folds to "main"
    ///     rather than to an empty host, because <c>new Uri("marten://")</c> is not a uri either.
    /// </remarks>
    internal static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return StoreOptions.DefaultStoreName.ToLowerInvariant();

        var builder = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '-');
        }

        var sanitized = builder.ToString().Trim('-');

        return sanitized.Length == 0 ? StoreOptions.DefaultStoreName.ToLowerInvariant() : sanitized;
    }

    /// <summary>
    ///     #5039: a closed generic marker interface (e.g. <c>IMartenStoreMarker&lt;MyContext&gt;</c>) has
    ///     a CLR type name containing a backtick and arity, which is not a valid uri hostname. Strip the
    ///     arity and fold in the generic argument names so distinct closed generics still map to
    ///     distinct uris.
    /// </summary>
    internal static string Sanitize(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0)
        {
            name = name.Substring(0, tick);
        }

        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments().Select(Sanitize);
            name = name + "-" + string.Join("-", arguments);
        }

        return Sanitize(name);
    }
}
