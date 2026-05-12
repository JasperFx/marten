using System;
using System.IO;
using JasperFx.CodeGeneration.Snapshots;
using Marten;
using Marten.Internal.CodeGeneration;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Internal.CodeGeneration;

/// <summary>
///     Filesystem round-trip + verify tests for the codegen snapshot
///     (marten#4370 Phase 2). Covers <see cref="MartenSnapshot.PersistFingerprint"/>
///     and <see cref="MartenSnapshot.VerifyAtBoot"/> end-to-end via a real
///     temp directory. No Postgres connection required.
/// </summary>
public class MartenSnapshotPersistenceTests : IDisposable
{
    private readonly string _tempFolder;

    public MartenSnapshotPersistenceTests()
    {
        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "marten-snapshot-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, recursive: true);
    }

    private StoreOptions OptionsAtTempFolder()
    {
        var opts = new StoreOptions();
        opts.Connection(ConnectionSource.ConnectionString);
        opts.GeneratedCodeOutputPath = _tempFolder;
        return opts;
    }

    // ─── First-boot path ─────────────────────────────────────────────────

    [Fact]
    public void verify_at_boot_returns_FirstBoot_when_folder_is_empty()
    {
        // No fingerprint persisted yet → FirstBoot. This is the steady-state
        // result on a fresh deployment, not an error.
        MartenSnapshot.VerifyAtBoot(OptionsAtTempFolder())
            .ShouldBe(SnapshotVerdict.FirstBoot);
    }

    [Fact]
    public void persist_then_verify_round_trips_to_Accept()
    {
        // The plumbing's primary contract: PersistFingerprint() followed by
        // VerifyAtBoot() with the same options accepts the snapshot. If this
        // ever stops holding, the boot-time verify path is broken.
        var opts = OptionsAtTempFolder();

        MartenSnapshot.PersistFingerprint(opts);

        MartenSnapshot.VerifyAtBoot(opts).ShouldBe(SnapshotVerdict.Accept);
    }

    [Fact]
    public void persist_writes_fingerprint_json_to_the_generated_folder()
    {
        MartenSnapshot.PersistFingerprint(OptionsAtTempFolder());

        var path = Path.Combine(_tempFolder, SnapshotGate.FingerprintFileName);
        File.Exists(path).ShouldBeTrue();
    }

    // ─── Mutation invalidates ───────────────────────────────────────────

    [Fact]
    public void mutation_after_persist_yields_RejectAndRegenerate()
    {
        // Establish a baseline snapshot on disk.
        var baseline = OptionsAtTempFolder();
        MartenSnapshot.PersistFingerprint(baseline);

        // Live boot with a mutated config — same temp folder, different inputs.
        var mutated = OptionsAtTempFolder();
        mutated.Schema.For<TestDoc>();

        MartenSnapshot.VerifyAtBoot(mutated)
            .ShouldBe(SnapshotVerdict.RejectAndRegenerate);
    }

    [Fact]
    public void rewriting_with_mutated_options_overwrites_the_baseline()
    {
        var baseline = OptionsAtTempFolder();
        MartenSnapshot.PersistFingerprint(baseline);

        var mutated = OptionsAtTempFolder();
        mutated.Schema.For<TestDoc>();
        MartenSnapshot.PersistFingerprint(mutated);

        // After overwrite the mutated options should now Accept (the persisted
        // fingerprint is the mutated one). This is the steady-state of the
        // "fall back to live, then re-persist" flow on the next boot.
        MartenSnapshot.VerifyAtBoot(mutated).ShouldBe(SnapshotVerdict.Accept);

        // And the original baseline should now be rejected because the
        // persisted fingerprint reflects the mutation.
        MartenSnapshot.VerifyAtBoot(baseline)
            .ShouldBe(SnapshotVerdict.RejectAndRegenerate);
    }

    // ─── Defensive behaviour ────────────────────────────────────────────

    [Fact]
    public void verify_at_boot_with_malformed_fingerprint_treats_as_FirstBoot()
    {
        // SnapshotGate.Read returns null on malformed JSON (soft-fallback policy).
        // From MartenSnapshot's perspective that's indistinguishable from no
        // fingerprint — both return FirstBoot. Pin this so a corrupt file in
        // the generated folder doesn't crash boot.
        File.WriteAllText(
            Path.Combine(_tempFolder, SnapshotGate.FingerprintFileName),
            "{ corrupted");

        MartenSnapshot.VerifyAtBoot(OptionsAtTempFolder())
            .ShouldBe(SnapshotVerdict.FirstBoot);
    }

    public class TestDoc
    {
        public Guid Id { get; set; }
    }
}
