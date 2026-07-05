using System;
using NSubstitute;
using Shouldly;
using Weasel.Core.Identity;
using Weasel.Core.Sequences;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// Tests for the <see cref="IIdentification{TDoc, TId}"/> contract now shared out of
/// Weasel.Core.Identity (#4811). Exercises each strategy against a substituted
/// <see cref="ISequenceSource"/> so the shape is testable without a real Postgres
/// roundtrip. Tracking: jasperfx/marten#4404 (W3), marten#4811.
/// </summary>
public class identification_strategy_tests
{
    public class GuidDoc { public Guid Id { get; set; } }
    public class IntDoc { public int Id { get; set; } }
    public class LongDoc { public long Id { get; set; } }

    // ─────────────────────────── SequentialGuid ───────────────────────────

    [Fact]
    public void sequential_guid__identity_reads_the_id_member()
    {
        var strategy = NewSequentialGuid();
        var id = Guid.NewGuid();

        strategy.Identity(new GuidDoc { Id = id }).ShouldBe(id);
    }

    [Fact]
    public void sequential_guid__assign_if_missing_generates_when_empty()
    {
        var strategy = NewSequentialGuid();
        var doc = new GuidDoc(); // Id == Guid.Empty
        var sequences = Substitute.For<ISequenceSource>();

        var assigned = strategy.AssignIfMissing(doc, sequences);

        assigned.ShouldNotBe(Guid.Empty);
        doc.Id.ShouldBe(assigned);
    }

    [Fact]
    public void sequential_guid__assign_if_missing_is_idempotent()
    {
        var strategy = NewSequentialGuid();
        var pre = Guid.NewGuid();
        var doc = new GuidDoc { Id = pre };
        var sequences = Substitute.For<ISequenceSource>();

        strategy.AssignIfMissing(doc, sequences).ShouldBe(pre);
        doc.Id.ShouldBe(pre);
    }

    [Fact]
    public void sequential_guid__does_not_touch_the_sequence_source()
    {
        // No round-trip — sequential-GUID generation is client-side. The
        // strategy must not resolve a sequence.
        var strategy = NewSequentialGuid();
        var doc = new GuidDoc();
        var sequences = Substitute.For<ISequenceSource>();

        strategy.AssignIfMissing(doc, sequences);

        sequences.DidNotReceive().SequenceFor(Arg.Any<Type>());
    }

    // ─────────────────────────── HiloInt ───────────────────────────

    [Fact]
    public void hilo_int__identity_reads_the_id_member()
    {
        var strategy = NewHiloInt();
        strategy.Identity(new IntDoc { Id = 42 }).ShouldBe(42);
    }

    [Fact]
    public void hilo_int__assign_if_missing_uses_the_per_type_sequence()
    {
        var strategy = NewHiloInt();
        var doc = new IntDoc();
        var (sequences, sequence) = NewSequenceSource(typeof(IntDoc));
        sequence.NextInt().Returns(7);

        var assigned = strategy.AssignIfMissing(doc, sequences);

        assigned.ShouldBe(7);
        doc.Id.ShouldBe(7);
        sequences.Received().SequenceFor(typeof(IntDoc));
        sequence.Received().NextInt();
    }

    [Fact]
    public void hilo_int__assign_if_missing_is_idempotent_for_positive_ids()
    {
        var strategy = NewHiloInt();
        var doc = new IntDoc { Id = 99 };
        var (sequences, sequence) = NewSequenceSource(typeof(IntDoc));

        strategy.AssignIfMissing(doc, sequences).ShouldBe(99);
        doc.Id.ShouldBe(99);
        sequence.DidNotReceive().NextInt();
    }

    // ─────────────────────────── HiloLong ───────────────────────────

    [Fact]
    public void hilo_long__identity_reads_the_id_member()
    {
        var strategy = NewHiloLong();
        strategy.Identity(new LongDoc { Id = 42L }).ShouldBe(42L);
    }

    [Fact]
    public void hilo_long__assign_if_missing_uses_the_per_type_sequence()
    {
        var strategy = NewHiloLong();
        var doc = new LongDoc();
        var (sequences, sequence) = NewSequenceSource(typeof(LongDoc));
        sequence.NextLong().Returns(123_456_789L);

        var assigned = strategy.AssignIfMissing(doc, sequences);

        assigned.ShouldBe(123_456_789L);
        doc.Id.ShouldBe(123_456_789L);
        sequences.Received().SequenceFor(typeof(LongDoc));
        sequence.Received().NextLong();
    }

    [Fact]
    public void hilo_long__assign_if_missing_is_idempotent_for_positive_ids()
    {
        var strategy = NewHiloLong();
        var doc = new LongDoc { Id = 99L };
        var (sequences, sequence) = NewSequenceSource(typeof(LongDoc));

        strategy.AssignIfMissing(doc, sequences).ShouldBe(99L);
        doc.Id.ShouldBe(99L);
        sequence.DidNotReceive().NextLong();
    }

    // ─────────────────────────── helpers ───────────────────────────

    // Accessor delegates inside each strategy are built via JasperFx's
    // LambdaBuilder (FEC-compiled). Tests pass the MemberInfo directly —
    // exactly the way DocumentStorage<T, TId> wires it today.

    private static SequentialGuidIdentification<GuidDoc> NewSequentialGuid()
        => new(typeof(GuidDoc).GetProperty(nameof(GuidDoc.Id))!);

    private static HiloIntIdentification<IntDoc> NewHiloInt()
        => new(typeof(IntDoc).GetProperty(nameof(IntDoc.Id))!, typeof(IntDoc));

    private static HiloLongIdentification<LongDoc> NewHiloLong()
        => new(typeof(LongDoc).GetProperty(nameof(LongDoc.Id))!, typeof(LongDoc));

    private static (ISequenceSource Sequences, ISequence Sequence) NewSequenceSource(Type docType)
    {
        var sequence = Substitute.For<ISequence>();
        var sequences = Substitute.For<ISequenceSource>();
        sequences.SequenceFor(docType).Returns(sequence);
        return (sequences, sequence);
    }
}
