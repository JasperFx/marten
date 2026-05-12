using System;
using JasperFx.CodeGeneration.Snapshots;
using Marten;
using Marten.Internal.CodeGeneration;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Internal.CodeGeneration;

/// <summary>
///     Trust-gate tests for the codegen snapshot canonical-input shape (marten#4370
///     Phase 2). These pin determinism + mutation-sensitivity. They run without
///     Postgres (canonical-input is a pure function of <see cref="StoreOptions"/>
///     and doesn't touch the database).
/// </summary>
public class MartenSnapshotInputsTests
{
    /// <summary>
    ///     Build a minimal StoreOptions that's still composed enough to walk.
    ///     We never call ApplyConfiguration / Validate / DocumentStore.For —
    ///     those open a database connection. We work directly against a
    ///     constructed StoreOptions and exercise the canonical-input shape.
    /// </summary>
    private static StoreOptions FreshOptions()
    {
        var opts = new StoreOptions();
        opts.Connection(ConnectionSource.ConnectionString);
        return opts;
    }

    // ─── Determinism ─────────────────────────────────────────────────────

    [Fact]
    public void compose_is_deterministic_for_unchanged_options()
    {
        var a = MartenSnapshotInputs.Compose(FreshOptions());
        var b = MartenSnapshotInputs.Compose(FreshOptions());

        a.ShouldBe(b);
    }

    [Fact]
    public void fingerprint_is_deterministic_for_unchanged_options()
    {
        var a = MartenSnapshot.BuildFingerprint(FreshOptions());
        var b = MartenSnapshot.BuildFingerprint(FreshOptions());

        a.ConfigHash.ShouldBe(b.ConfigHash);
        a.ShouldBe(b);
    }

    [Fact]
    public void config_hash_is_a_lowercase_64_char_hex_digest()
    {
        var fp = MartenSnapshot.BuildFingerprint(FreshOptions());

        fp.ConfigHash.Length.ShouldBe(64);
        fp.ConfigHash.ShouldAllBe(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }

    [Fact]
    public void fingerprint_records_marten_product_name()
    {
        MartenSnapshot.BuildFingerprint(FreshOptions()).ProductName.ShouldBe("marten");
    }

    // ─── Mutation matrix ────────────────────────────────────────────────
    // Each mutation tests one dimension of MartenSnapshotInputs.Compose and
    // verifies the resulting fingerprint differs. If any of these stop
    // failing, you've broken invalidation for that input — bad.

    [Fact]
    public void mutation_adding_a_document_type_changes_the_hash()
    {
        var baseline = MartenSnapshot.BuildFingerprint(FreshOptions());

        var mutated = FreshOptions();
        mutated.Schema.For<DocTypeA>();
        var withDoc = MartenSnapshot.BuildFingerprint(mutated);

        withDoc.ConfigHash.ShouldNotBe(baseline.ConfigHash);
    }

    [Fact]
    public void mutation_different_document_type_yields_different_hash()
    {
        var a = FreshOptions();
        a.Schema.For<DocTypeA>();
        var hashA = MartenSnapshot.BuildFingerprint(a).ConfigHash;

        var b = FreshOptions();
        b.Schema.For<DocTypeB>();
        var hashB = MartenSnapshot.BuildFingerprint(b).ConfigHash;

        hashA.ShouldNotBe(hashB);
    }

    [Fact]
    public void mutation_changing_database_schema_name_changes_the_hash()
    {
        var baseline = MartenSnapshot.BuildFingerprint(FreshOptions());

        var mutated = FreshOptions();
        mutated.DatabaseSchemaName = "different_schema";
        var withCustomSchema = MartenSnapshot.BuildFingerprint(mutated);

        withCustomSchema.ConfigHash.ShouldNotBe(baseline.ConfigHash);
    }

    [Fact]
    public void mutation_changing_store_name_changes_the_hash()
    {
        var baseline = MartenSnapshot.BuildFingerprint(FreshOptions());

        var mutated = FreshOptions();
        mutated.StoreName = "AnotherStore";
        var withCustomName = MartenSnapshot.BuildFingerprint(mutated);

        withCustomName.ConfigHash.ShouldNotBe(baseline.ConfigHash);
    }

    [Fact]
    public void mutation_changing_stream_identity_changes_the_hash()
    {
        var baseline = MartenSnapshot.BuildFingerprint(FreshOptions());

        var mutated = FreshOptions();
        mutated.Events.StreamIdentity = JasperFx.Events.StreamIdentity.AsString;
        var withStringStream = MartenSnapshot.BuildFingerprint(mutated);

        withStringStream.ConfigHash.ShouldNotBe(baseline.ConfigHash);
    }

    [Fact]
    public void mutation_enabling_strict_stream_identity_changes_the_hash()
    {
        var baseline = MartenSnapshot.BuildFingerprint(FreshOptions());

        var mutated = FreshOptions();
        mutated.Events.EnableStrictStreamIdentityEnforcement = true;
        var withStrict = MartenSnapshot.BuildFingerprint(mutated);

        withStrict.ConfigHash.ShouldNotBe(baseline.ConfigHash);
    }

    [Fact]
    public void mutation_enabling_extended_progression_tracking_changes_the_hash()
    {
        // EventGraph is internal so we access the flag via the internal property
        // rather than the IEventStoreOptions public surface. CoreTests sees Marten
        // internals via InternalsVisibleTo.
        var baseline = MartenSnapshot.BuildFingerprint(FreshOptions());

        var mutated = FreshOptions();
        mutated.EventGraph.EnableExtendedProgressionTracking = true;
        var withExt = MartenSnapshot.BuildFingerprint(mutated);

        withExt.ConfigHash.ShouldNotBe(baseline.ConfigHash);
    }

    // ─── Composition / order-stability ──────────────────────────────────

    [Fact]
    public void doc_type_registration_order_does_not_affect_hash()
    {
        // Sorting in MartenSnapshotInputs means two stores that register the same
        // types in different orders should produce identical fingerprints. If
        // this test fails the sort isn't doing its job and downstream snapshots
        // would spuriously invalidate on cosmetic option-build reorderings.
        var a = FreshOptions();
        a.Schema.For<DocTypeA>();
        a.Schema.For<DocTypeB>();
        var hashA = MartenSnapshot.BuildFingerprint(a).ConfigHash;

        var b = FreshOptions();
        b.Schema.For<DocTypeB>();
        b.Schema.For<DocTypeA>();
        var hashB = MartenSnapshot.BuildFingerprint(b).ConfigHash;

        hashA.ShouldBe(hashB);
    }

    // ─── Verdict via SnapshotGate ───────────────────────────────────────

    [Fact]
    public void verify_returns_Accept_when_persisted_matches_live()
    {
        var live = MartenSnapshot.BuildFingerprint(FreshOptions());
        SnapshotGate.Verify(live, persisted: live).ShouldBe(SnapshotVerdict.Accept);
    }

    [Fact]
    public void verify_returns_FirstBoot_when_no_persisted_fingerprint()
    {
        var live = MartenSnapshot.BuildFingerprint(FreshOptions());
        SnapshotGate.Verify(live, persisted: null).ShouldBe(SnapshotVerdict.FirstBoot);
    }

    [Fact]
    public void verify_returns_RejectAndRegenerate_when_options_mutated()
    {
        var persisted = MartenSnapshot.BuildFingerprint(FreshOptions());

        var live = FreshOptions();
        live.Schema.For<DocTypeA>();
        var liveFingerprint = MartenSnapshot.BuildFingerprint(live);

        SnapshotGate.Verify(liveFingerprint, persisted)
            .ShouldBe(SnapshotVerdict.RejectAndRegenerate);
    }

    // ─── Test fixtures ──────────────────────────────────────────────────

    public class DocTypeA
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class DocTypeB
    {
        public Guid Id { get; set; }
        public int Count { get; set; }
    }
}
