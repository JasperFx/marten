#nullable enable
using System;
using System.IO;
using System.Reflection;
using JasperFx.CodeGeneration.Snapshots;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal.CodeGeneration;

/// <summary>
///     Phase 2 of <see href="https://github.com/JasperFx/marten/issues/4370">marten#4370</see>.
///     Marten-side consumer of the codegen snapshot invalidation contract that JasperFx 2.0
///     ships (<see cref="SnapshotGate"/>). Wires <see cref="StoreOptions"/> into the shared
///     fingerprint persistence + verdict flow.
/// </summary>
/// <remarks>
///     <para>
///     <b>What Phase 2 ships (this class).</b> Plumbing: compute Marten's canonical input
///     hash via <see cref="MartenSnapshotInputs"/>, persist the fingerprint to disk via
///     <see cref="SnapshotGate.Write"/>, verify at boot via <see cref="SnapshotGate.Verify"/>,
///     log the verdict via <see cref="SnapshotGate.SnapshotRejectedLogTemplate"/>. The verdict
///     is computed but does <b>not yet</b> gate any expensive boot work — Phase 2 deliberately
///     stops at the plumbing so subsequent PRs can add concrete artifacts (event-name map,
///     document-storage map, projection apply factories) incrementally, each gated by the
///     verdict that's already being computed.
///     </para>
///     <para>
///     <b>Fingerprint scope.</b> One fingerprint per <see cref="StoreOptions"/> instance,
///     persisted as <c>fingerprint.json</c> in the store's generated-code output path.
///     Multi-store hosts get one fingerprint per store; the <see cref="StoreOptions.StoreName"/>
///     contributes to <see cref="MartenSnapshotInputs.Compose"/> so two stores at the same
///     output path don't collide.
///     </para>
///     <para>
///     <b>Soft-fallback policy.</b> Per the <see cref="SnapshotGate"/> contract, a rejected
///     snapshot logs via <see cref="SnapshotGate.SnapshotRejectedLogTemplate"/> at
///     <see cref="LogLevel.Information"/> and the consumer continues with the live discovery
///     path. Operators grep for <c>JasperFx.Codegen: snapshot rejected</c> when diagnosing
///     slow first-boot-after-deploy.
///     </para>
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal static class MartenSnapshot
{
    private const string ProductName = "marten";

    /// <summary>
    ///     Read + verify any persisted snapshot, log the verdict, and return it. Caller
    ///     can use the verdict to drive Phase 3+ artifact-loading decisions; Phase 2
    ///     just logs and returns.
    /// </summary>
    /// <param name="options">Fully-composed store options.</param>
    /// <param name="logger">
    ///     Optional logger; when <see langword="null"/>, log calls are skipped. The
    ///     log signature on rejection is <see cref="SnapshotGate.SnapshotRejectedLogTemplate"/>
    ///     and is part of the public stability contract.
    /// </param>
    internal static SnapshotVerdict VerifyAtBoot(StoreOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var folder = ResolveSnapshotFolder(options);
        if (folder is null)
        {
            // No generated-code output path is configured (and the default
            // resolution couldn't produce one either) — there's nowhere to
            // read a snapshot from, so treat as first boot.
            return SnapshotVerdict.FirstBoot;
        }

        var live = BuildFingerprint(options);
        var persisted = SnapshotGate.Read(folder);
        var verdict = SnapshotGate.Verify(live, persisted);

        if (verdict == SnapshotVerdict.RejectAndRegenerate && logger is not null)
        {
            // Public log signature — see SnapshotGate.SnapshotRejectedLogTemplate
            // remarks. Operators key off this exact prefix when diagnosing.
#pragma warning disable CA2254 // Template not constant — the template IS the constant; placeholders bind at the structured-log level
            logger.LogInformation(
                SnapshotGate.SnapshotRejectedLogTemplate,
                ProductName,
                live.ProductVersion + "/" + (options.StoreName ?? ""),
                DescribeReason(live, persisted!));
#pragma warning restore CA2254
        }

        return verdict;
    }

    /// <summary>
    ///     Persist the current fingerprint to the store's generated-code folder. Called
    ///     at the end of <see cref="DocumentStore"/> construction so the next boot's
    ///     <see cref="VerifyAtBoot"/> can compare against a freshly-written value. Idempotent:
    ///     re-running with the same options produces the same fingerprint file content.
    /// </summary>
    /// <remarks>
    ///     <b>Why persist on every boot, not just at <c>codegen write</c> time?</b>
    ///     Phase 2 plumbing keeps the snapshot path live in every mode so the contract
    ///     is exercised whether or not the user runs the CLI. Future phases can gate
    ///     persistence on the consumer actually writing artifacts (no point persisting
    ///     a fingerprint with nothing to invalidate), but at Phase 2 the persistence
    ///     itself is what's being validated.
    /// </remarks>
    internal static void PersistFingerprint(StoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var folder = ResolveSnapshotFolder(options);
        if (folder is null) return;

        try
        {
            SnapshotGate.Write(folder, BuildFingerprint(options));
        }
        catch (Exception)
        {
            // Persistence is a cold-start optimisation, not a correctness
            // requirement. Failure to write the fingerprint must not break
            // DocumentStore construction. Swallow silently — the next boot
            // will be FirstBoot, which is also a no-op.
        }
    }

    /// <summary>
    ///     Build a <see cref="SnapshotFingerprint"/> for the given options. Public-internal
    ///     so tests can call it directly without going through the file-system round-trip.
    /// </summary>
    internal static SnapshotFingerprint BuildFingerprint(StoreOptions options)
    {
        var canonical = MartenSnapshotInputs.Compose(options);
        return new SnapshotFingerprint(
            ProductName: ProductName,
            ProductVersion: MartenVersion,
            JasperFxVersion: JasperFxVersion,
            ConfigHash: SnapshotGate.ComputeHash(canonical));
    }

    private static string? ResolveSnapshotFolder(StoreOptions options)
    {
        // The snapshot lives alongside Marten's generated code. CreateGenerationRules
        // resolves the path with the same fallback logic that codegen-write uses
        // (explicit GeneratedCodeOutputPath → AppContext.BaseDirectory/Internal/Generated,
        // with StoreName appended for non-Main stores).
        try
        {
            var rules = options.CreateGenerationRules();
            return string.IsNullOrWhiteSpace(rules.GeneratedCodeOutputPath)
                ? null
                : rules.GeneratedCodeOutputPath;
        }
        catch
        {
            // Best-effort: if the rules can't be built (e.g. test scenarios with
            // half-configured options) we treat that as "no folder" and skip both
            // verify and persist.
            return null;
        }
    }

    private static string DescribeReason(SnapshotFingerprint live, SnapshotFingerprint persisted)
    {
        // Identify the field(s) that changed so the rejection log is actionable.
        // Order matches typical change probability: config first (most common
        // dev-time churn), then versions (which point at upgrades).
        if (live.ConfigHash != persisted.ConfigHash) return "config-hash changed";
        if (live.ProductVersion != persisted.ProductVersion) return $"marten version {persisted.ProductVersion} → {live.ProductVersion}";
        if (live.JasperFxVersion != persisted.JasperFxVersion) return $"jasperfx version {persisted.JasperFxVersion} → {live.JasperFxVersion}";
        if (live.SchemaVersion != persisted.SchemaVersion) return $"snapshot schema {persisted.SchemaVersion} → {live.SchemaVersion}";
        return "fingerprint mismatch";
    }

    private static readonly string MartenVersion =
        typeof(StoreOptions).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static readonly string JasperFxVersion =
        typeof(SnapshotGate).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
