#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using JasperFx.CodeGeneration;
using Marten.Events;

namespace Marten.Internal.CodeGeneration;

/// <summary>
///     Phase 2 of <see href="https://github.com/JasperFx/marten/issues/4370">marten#4370</see>.
///     Builds the canonical-input string that <see cref="JasperFx.CodeGeneration.Snapshots.SnapshotGate.ComputeHash"/>
///     hashes into <see cref="JasperFx.CodeGeneration.Snapshots.SnapshotFingerprint.ConfigHash"/>.
///     The fingerprint invalidates whenever any input that would change the generated codegen
///     output (or the dispatch tables a future snapshot artifact would persist) changes.
/// </summary>
/// <remarks>
///     <para>
///     <b>Determinism requirements.</b> The canonical string must be stable across runs given
///     the same <see cref="StoreOptions"/>. That means: collections are sorted, value
///     representations are normalised (e.g. <see cref="Type.AssemblyQualifiedName"/> rather
///     than instance hashes), and there are no environment-dependent inputs (no
///     <see cref="Environment.MachineName"/>, no <see cref="DateTime.UtcNow"/>, etc.).
///     </para>
///     <para>
///     <b>Input scope.</b> The inputs are everything that can plausibly affect the boot-time
///     state a snapshot artifact would persist:
///     </para>
///     <list type="bullet">
///       <item>Marten version</item>
///       <item><see cref="StoreOptions.StoreName"/></item>
///       <item>The registered document-type list (AssemblyQualifiedName, sorted)</item>
///       <item>The registered event-type alias list (alias + AssemblyQualifiedName pairs, sorted by alias)</item>
///       <item>The registered projection-type list (AssemblyQualifiedName, sorted)</item>
///       <item>Serializer type</item>
///       <item>Boot-relevant <see cref="StoreOptions"/> flags
///         (<see cref="StoreOptions.DatabaseSchemaName"/>,
///         <see cref="StoreOptions.GeneratedCodeMode"/>,
///         <see cref="EventGraph.StreamIdentity"/>,
///         <see cref="EventGraph.EnableExtendedProgressionTracking"/>,
///         <see cref="EventGraph.EnableStrictStreamIdentityEnforcement"/>)</item>
///     </list>
///     <para>
///     The JasperFx version is supplied via the <see cref="JasperFx.CodeGeneration.Snapshots.SnapshotFingerprint"/>
///     itself (separate field), not folded into the canonical input — invalidation on a JasperFx
///     upgrade comes for free from the fingerprint comparison.
///     </para>
/// </remarks>
internal static class MartenSnapshotInputs
{
    /// <summary>
    ///     Sentinel inserted between input groups in the canonical string. Picked to be
    ///     unambiguous against the AssemblyQualifiedName / alias / flag-value content,
    ///     none of which contain U+241E (record separator).
    /// </summary>
    private const char GroupSeparator = '␞';

    /// <summary>
    ///     Inter-record separator within a group (e.g. between two doc-type AQNs).
    ///     Same rationale — U+241F is record-unit separator and won't appear in CLR
    ///     names or string flag values.
    /// </summary>
    private const char RecordSeparator = '␟';

    /// <summary>
    ///     Build the canonical-input string from a fully-composed <see cref="StoreOptions"/>.
    ///     Caller is responsible for ensuring all registrations (doc types, projections,
    ///     event mappings) have completed before invoking this — typically after
    ///     <see cref="StorageFeatures.PostProcessConfiguration"/> + the event-graph init pass.
    /// </summary>
    internal static string Compose(StoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Force materialisation of any pending DocumentMappingBuilder<T>
        // registrations from opts.Schema.For<T>() before enumerating. Per
        // marten#4303, AllDocumentMappings is lazy and only returns mappings
        // that have been built; without this call the canonical-input misses
        // any types registered after the last build. BuildAllMappings is
        // idempotent — subsequent calls are no-ops.
        options.Storage.BuildAllMappings();

        var sb = new StringBuilder(capacity: 1024);

        AppendGroup(sb, "marten-version", typeof(StoreOptions).Assembly.GetName().Version?.ToString() ?? "");
        AppendGroup(sb, "store-name", options.StoreName ?? "");

        // Document types — sort by AQN for stable ordering across runs.
        var docTypes = options.Storage
            .AllDocumentMappings
            .Select(m => m.DocumentType.AssemblyQualifiedName ?? m.DocumentType.FullName ?? "")
            .Where(s => s.Length > 0)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        AppendList(sb, "doc-types", docTypes);

        // Event-type aliases — sort by alias. Pair = alias + AQN so an aliased
        // rename of the same underlying type invalidates the snapshot.
        var eventAliases = options.EventGraph.AllEvents()
            .Select(m => m.EventTypeName + "=" + (m.DocumentType.AssemblyQualifiedName ?? ""))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        AppendList(sb, "event-aliases", eventAliases);

        // Projection types — sort by AQN. PublishedTypes is part of the
        // IProjectionSource contract and provides a stable identifier.
        var projectionTypes = options.Projections.All
            .Select(p => p.GetType().AssemblyQualifiedName ?? p.GetType().FullName ?? "")
            .Where(s => s.Length > 0)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        AppendList(sb, "projection-types", projectionTypes);

        // Serializer — affects JSON shape, which affects generated code.
        AppendGroup(sb, "serializer-type",
            options.Serializer().GetType().AssemblyQualifiedName ?? "");

        // Boot-relevant flags. Each new entry here is a deliberate design call —
        // only flags that would change the generated output or the dispatch tables
        // belong. Adding a flag here will invalidate every existing snapshot the
        // first time it's read, by design.
        AppendGroup(sb, "schema-name", options.DatabaseSchemaName ?? "");
        AppendGroup(sb, "code-mode", options.GeneratedCodeMode.ToString());
        AppendGroup(sb, "stream-identity", options.EventGraph.StreamIdentity.ToString());
        AppendGroup(sb, "ext-progression-tracking",
            options.EventGraph.EnableExtendedProgressionTracking.ToString());
        AppendGroup(sb, "strict-stream-identity",
            options.EventGraph.EnableStrictStreamIdentityEnforcement.ToString());

        return sb.ToString();
    }

    private static void AppendGroup(StringBuilder sb, string key, string value)
    {
        if (sb.Length > 0) sb.Append(GroupSeparator);
        sb.Append(key).Append('=').Append(value);
    }

    private static void AppendList(StringBuilder sb, string key, string[] items)
    {
        if (sb.Length > 0) sb.Append(GroupSeparator);
        sb.Append(key).Append('=');
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0) sb.Append(RecordSeparator);
            sb.Append(items[i]);
        }
    }
}
