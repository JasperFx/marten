using System;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Events;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Schema.Identity;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

/// <summary>
///     https://github.com/JasperFx/marten/issues/5562.
/// </summary>
/// <remarks>
///     <para>
///     Identity resolution handed its candidate filter to <c>JasperFx.DocumentIdentity.FindIdMember</c>,
///     which applies the filter to EVERY property and field and only then picks the id by
///     <c>[Identity]</c> or by the name <c>Id</c>. The filter was the effectful one: for any public
///     struct shaped like a strong-typed id, <c>ValueTypeIdGeneration.IsCandidate</c> registers that
///     type on the GLOBAL <see cref="PostgresqlProvider" /> singleton and constructs a
///     <c>ValueTypeIdGeneration</c>, whose constructor closes <c>ValueTypeIdSelectClause&lt;,&gt;</c>.
///     </para>
///     <para>
///     So an ordinary non-identity property of such a type got remapped process-wide the moment its
///     document or event type was mapped — turning <c>Optional&lt;string&gt;</c> into <c>varchar</c> for
///     everything, including LINQ — and under Native AOT the generic construction took startup down
///     with a <c>MissingMethodException</c>.
///     </para>
///     <para>
///     Every test here uses its own nested types. <see cref="PostgresqlProvider.Instance" /> is a
///     process-wide singleton, so a shared probe type would let one test's registration decide another
///     test's result.
///     </para>
/// </remarks>
public class Bug_5562_identity_probe_side_effects
{
    private static string dbTypeOf(Type type)
        => PostgresqlProvider.Instance.GetDatabaseType(type, EnumStorage.AsInteger);

    // ------------------------------------------------- the probe must not register bystanders

    [Fact]
    public void a_non_identity_value_struct_is_not_remapped()
    {
        var before = dbTypeOf(typeof(Optional<string>));

        var mapping = new DocumentMapping(typeof(CustomerWithOptional), new StoreOptions());

        mapping.IdMember.Name.ShouldBe(nameof(CustomerWithOptional.Id));
        dbTypeOf(typeof(Optional<string>)).ShouldBe(before);
    }

    [Fact]
    public void a_non_identity_value_struct_on_an_EVENT_type_is_not_remapped()
    {
        // The reported Native AOT crash came through EventMapping, not DocumentMapping.
        var before = dbTypeOf(typeof(Patch<string>));

        var options = new StoreOptions();
        _ = new EventMapping<NicknameChanged>(options.EventGraph);

        dbTypeOf(typeof(Patch<string>)).ShouldBe(before);
    }

    [Fact]
    public void a_guid_backed_bystander_struct_is_not_remapped()
    {
        var before = dbTypeOf(typeof(GuidWrapper));

        var mapping = new DocumentMapping(typeof(CustomerWithGuidWrapper), new StoreOptions());

        mapping.IdMember.Name.ShouldBe(nameof(CustomerWithGuidWrapper.Id));
        dbTypeOf(typeof(GuidWrapper)).ShouldBe(before);
    }

    [Fact]
    public void a_bystander_built_by_a_static_factory_rather_than_a_ctor_is_not_remapped()
    {
        var before = dbTypeOf(typeof(FactoryBuilt));

        var mapping = new DocumentMapping(typeof(CustomerWithFactoryBuilt), new StoreOptions());

        mapping.IdMember.Name.ShouldBe(nameof(CustomerWithFactoryBuilt.Id));
        dbTypeOf(typeof(FactoryBuilt)).ShouldBe(before);
    }

    [Fact]
    public void a_bystander_is_not_remapped_when_the_id_is_itself_strong_typed()
    {
        var before = dbTypeOf(typeof(Optional<int>));

        var mapping = new DocumentMapping(typeof(OrderWithStrongId), new StoreOptions());

        mapping.IdMember.Name.ShouldBe(nameof(OrderWithStrongId.Id));
        dbTypeOf(typeof(Optional<int>)).ShouldBe(before);
    }

    [Fact]
    public void a_bystander_is_not_remapped_when_the_id_is_chosen_by_attribute()
    {
        var before = dbTypeOf(typeof(Optional<long>));

        var mapping = new DocumentMapping(typeof(CustomerWithAttributeId), new StoreOptions());

        mapping.IdMember.Name.ShouldBe(nameof(CustomerWithAttributeId.Key));
        dbTypeOf(typeof(Optional<long>)).ShouldBe(before);
    }

    // ------------------------------------------------- the real id still registers

    [Fact]
    public void a_strong_typed_id_is_still_resolved_and_registered()
    {
        var mapping = new DocumentMapping(typeof(OrderWithStrongId), new StoreOptions());

        mapping.IdMember.Name.ShouldBe(nameof(OrderWithStrongId.Id));
        mapping.IdMember.GetMemberType().ShouldBe(typeof(OrderId));

        // The registration the effectful probe exists to perform must still happen for the member
        // that was actually chosen -- otherwise the id would not round-trip as a uuid column.
        dbTypeOf(typeof(OrderId)).ShouldBe("uuid");
    }

    [Fact]
    public void a_strong_typed_id_chosen_by_attribute_is_still_registered()
    {
        var mapping = new DocumentMapping(typeof(InvoiceWithAttributeStrongId), new StoreOptions());

        mapping.IdMember.Name.ShouldBe(nameof(InvoiceWithAttributeStrongId.Number));
        dbTypeOf(typeof(InvoiceId)).ShouldBe("varchar");
    }

    [Fact]
    public void a_strong_typed_id_built_by_a_static_factory_is_still_registered()
    {
        var mapping = new DocumentMapping(typeof(ShipmentWithFactoryId), new StoreOptions());

        mapping.IdMember.Name.ShouldBe(nameof(ShipmentWithFactoryId.Id));
        dbTypeOf(typeof(ShipmentId)).ShouldBe("integer");
    }

    [Fact]
    public void an_int_backed_strong_typed_id_is_still_registered()
    {
        var mapping = new DocumentMapping(typeof(TicketWithIntId), new StoreOptions());

        mapping.IdMember.Name.ShouldBe(nameof(TicketWithIntId.Id));
        dbTypeOf(typeof(TicketId)).ShouldBe("integer");
    }

    // ------------------------------------------------- the shape predicate agrees with the real one

    [Theory]
    [InlineData(typeof(Guid), true)]
    [InlineData(typeof(string), true)]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(long), true)]
    [InlineData(typeof(Guid?), true)]
    [InlineData(typeof(OrderId), true)]
    [InlineData(typeof(InvoiceId), true)]
    [InlineData(typeof(ShipmentId), true)]
    [InlineData(typeof(Optional<string>), true)]
    [InlineData(typeof(MultiPartValue), false)]
    [InlineData(typeof(NotAnId), false)]
    [InlineData(typeof(decimal), false)]
    [InlineData(typeof(DateTimeOffset), false)]
    [InlineData(typeof(object), false)]
    public void the_shape_predicate_accepts_exactly_what_the_effectful_one_accepts(Type type, bool expected)
    {
        // Same answer, no registration. If these ever diverge, identity resolution would pick a
        // different member than it used to -- which is the one regression this split could cause.
        DocumentMapping.IsPlausibleIdentityType(type).ShouldBe(expected);
        DocumentMapping.IsValidIdentityType(type).ShouldBe(expected);
    }

    [Fact]
    public void the_shape_predicate_registers_nothing()
    {
        var before = dbTypeOf(typeof(NeverRegistered));

        DocumentMapping.IsPlausibleIdentityType(typeof(NeverRegistered)).ShouldBeTrue();

        dbTypeOf(typeof(NeverRegistered)).ShouldBe(before);
    }

    [Fact]
    public void ValueTypeIdGeneration_IsCandidateShape_agrees_with_IsCandidate()
    {
        ValueTypeIdGeneration.IsCandidateShape(typeof(PairedProbe)).ShouldBeTrue();
        ValueTypeIdGeneration.IsCandidate(typeof(PairedProbe), out var generation).ShouldBeTrue();
        generation.ShouldNotBeNull();

        ValueTypeIdGeneration.IsCandidateShape(typeof(MultiPartValue)).ShouldBeFalse();
        ValueTypeIdGeneration.IsCandidate(typeof(MultiPartValue), out _).ShouldBeFalse();
    }

    // ------------------------------------------------- unchanged refusals

    [Fact]
    public void a_multi_property_value_object_is_still_refused()
    {
        // The pre-existing guard for `Money(decimal, Guid)` shapes, kept intact by the split.
        DocumentMapping.IsPlausibleIdentityType(typeof(MultiPartValue)).ShouldBeFalse();
        dbTypeOf(typeof(MultiPartValue)).ShouldBe("jsonb");
    }

    [Fact]
    public void a_document_with_no_usable_id_still_throws()
    {
        Should.Throw<InvalidDocumentException>(() =>
            new DocumentMapping(typeof(NoIdAtAll), new StoreOptions()).CompileAndValidate());
    }

    // ------------------------------------------------- probes

    public readonly struct Optional<T>
    {
        public Optional(T value) => Value = value;
        public T Value { get; }
    }

    public readonly struct Patch<T>
    {
        public Patch(T value) => Value = value;
        public T Value { get; }
    }

    public readonly struct GuidWrapper
    {
        public GuidWrapper(Guid value) => Value = value;
        public Guid Value { get; }
    }

    public readonly struct NeverRegistered
    {
        public NeverRegistered(string value) => Value = value;
        public string Value { get; }
    }

    public readonly struct PairedProbe
    {
        public PairedProbe(string value) => Value = value;
        public string Value { get; }
    }

    public readonly struct FactoryBuilt
    {
        private FactoryBuilt(long value) => Value = value;
        public long Value { get; }
        public static FactoryBuilt From(long value) => new(value);
    }

    public readonly struct MultiPartValue
    {
        public MultiPartValue(decimal amount, Guid currencyId)
        {
            Amount = amount;
            CurrencyId = currencyId;
        }

        public decimal Amount { get; }
        public Guid CurrencyId { get; }
    }

    public readonly struct NotAnId
    {
        public NotAnId(decimal value) => Value = value;
        public decimal Value { get; }
    }

    public readonly struct OrderId
    {
        public OrderId(Guid value) => Value = value;
        public Guid Value { get; }
    }

    public readonly struct InvoiceId
    {
        public InvoiceId(string value) => Value = value;
        public string Value { get; }
    }

    public readonly struct TicketId
    {
        public TicketId(int value) => Value = value;
        public int Value { get; }
    }

    public readonly struct ShipmentId
    {
        private ShipmentId(int value) => Value = value;
        public int Value { get; }
        public static ShipmentId From(int value) => new(value);
    }

    // ------------------------------------------------- documents

    public sealed class CustomerWithOptional
    {
        public Guid Id { get; set; }
        public Optional<string> Nickname { get; set; }
    }

    public sealed class CustomerWithGuidWrapper
    {
        public Guid Id { get; set; }
        public GuidWrapper Tracking { get; set; }
    }

    public sealed class CustomerWithFactoryBuilt
    {
        public Guid Id { get; set; }
        public FactoryBuilt Counter { get; set; }
    }

    public sealed class CustomerWithAttributeId
    {
        [Identity] public Guid Key { get; set; }
        public Optional<long> Measurement { get; set; }
    }

    public sealed class OrderWithStrongId
    {
        public OrderId Id { get; set; }
        public Optional<int> Quantity { get; set; }
    }

    public sealed class InvoiceWithAttributeStrongId
    {
        [Identity] public InvoiceId Number { get; set; }
    }

    public sealed class TicketWithIntId
    {
        public TicketId Id { get; set; }
    }

    public sealed class ShipmentWithFactoryId
    {
        public ShipmentId Id { get; set; }
    }

    public sealed class NoIdAtAll
    {
        public string Name { get; set; } = string.Empty;
    }

    public record NicknameChanged(Guid CustomerId, Patch<string> Nickname);
}
