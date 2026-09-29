namespace Marten.Exceptions;

/// <summary>
///     Thrown when a session or projection daemon is created against the default tenant while the store's
///     default tenant usage is disabled — the automatic state once database-per-tenant tenancy is configured.
/// </summary>
/// <remarks>
///     <para>
///         #5514 — this derives from <see cref="JasperFx.Events.DefaultTenantUsageDisabledException" />, which
///         was lifted from the byte-identically-messaged copies in Marten and Polecat and whose contract is
///         that stores subclass it. Polecat honored that; Marten did not, so a store-agnostic
///         <c>catch (JasperFx.Events.DefaultTenantUsageDisabledException)</c> compiled, read as handled, and
///         silently missed on Marten — an unhandled exception in code that looks like it handles it.
///     </para>
///     <para>
///         ⚠️ <b>Breaking:</b> this is therefore no longer a <see cref="MartenException" />. Multiple
///         inheritance is not available, so it is one or the other, and the cross-store catch was judged the
///         more valuable of the two: nothing in Marten narrows on <c>MartenException</c> here, and a store
///         misconfiguration is not the kind of failure a blanket Marten handler usefully recovers from.
///         <c>all_exceptions_should_derive_from_MartenException</c> names this type in its ignore list with
///         the same reason. The sibling reparenting in
///         <see href="https://github.com/JasperFx/marten/issues/5476">#5476</see> (ArchivedStreamException) is
///         a separate, wider decision and stays on 10.0.
///     </para>
///     <para>
///         The constructors delegate rather than reformat: the base already produces the exact message Marten
///         produced before, including the single-argument overload <em>appending</em> to the standard prefix.
///         The obsolete <c>SerializationInfo</c> constructor is gone with the reparenting — the JasperFx base
///         declares none to chain to, and binary serialization of exceptions is obsolete as of .NET 8.
///     </para>
/// </remarks>
public class DefaultTenantUsageDisabledException: JasperFx.Events.DefaultTenantUsageDisabledException
{
    public DefaultTenantUsageDisabledException()
    {
    }

    public DefaultTenantUsageDisabledException(string message): base(message)
    {
    }
}
