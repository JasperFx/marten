using System;
using Marten;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

#region promotion test events

/// <summary>
/// Marked with Marten's own attribute — the spelling every existing user wrote.
/// </summary>
[Marten.Events.BinaryEvent]
public record PromotionMartenMarked(string Name);

/// <summary>
/// Marked with the promoted JasperFx attribute — the spelling an event type shared by source
/// compiled against Marten, Polecat and Fisher can use.
/// </summary>
[JasperFx.Events.BinaryEvent]
public record PromotionCoreMarked(string Name);

public record PromotionUnmarked(string Name);

/// <summary>
/// Implements ONLY <see cref="JasperFx.Events.IEventBinarySerializer" /> — nothing Marten-namespaced.
/// Registering this at all is the whole point of the widening.
/// </summary>
public class CoreOnlyBinarySerializer: JasperFx.Events.IEventBinarySerializer
{
    public byte[] Serialize(Type type, object data) => throw new NotSupportedException("Not exercised here");

    public object Deserialize(Type type, byte[] data) => throw new NotSupportedException("Not exercised here");
}

/// <summary>
/// Implements the Marten-namespaced interface exactly as a pre-9.26 user would have. It must keep
/// compiling and must still be accepted by the widened registration surface.
/// </summary>
public class LegacyMartenBinarySerializer: Marten.Events.IEventBinarySerializer
{
    public byte[] Serialize(Type type, object data) => throw new NotSupportedException("Not exercised here");

    public object Deserialize(Type type, byte[] data) => throw new NotSupportedException("Not exercised here");
}

#endregion

/// <summary>
/// jasperfx#669: IEventBinarySerializer and BinaryEventAttribute were promoted out of
/// Marten.Events into JasperFx.Events. These pin the compatibility claims that promotion rests on.
/// No database — every assertion is about registration and resolution.
/// </summary>
public class binary_event_serializer_promotion
{
    [Fact]
    public void martens_own_binary_event_attribute_is_still_honored()
    {
        var options = new StoreOptions();
        options.Events.DefaultBinarySerializer = new CoreOnlyBinarySerializer();
        options.Events.AddEventType(typeof(PromotionMartenMarked));

        options.EventGraph.EventMappingFor(typeof(PromotionMartenMarked))
            .IsBinary.ShouldBeTrue();
    }

    [Fact]
    public void the_promoted_jasperfx_binary_event_attribute_is_honored()
    {
        var options = new StoreOptions();
        options.Events.DefaultBinarySerializer = new CoreOnlyBinarySerializer();
        options.Events.AddEventType(typeof(PromotionCoreMarked));

        options.EventGraph.EventMappingFor(typeof(PromotionCoreMarked))
            .IsBinary.ShouldBeTrue();
    }

    /// <summary>
    /// jasperfx#672: the promoted attribute was unsealed so Marten's own could derive from it, which
    /// is what lets ResolveBinarySerializerFor do ONE lookup instead of two. If someone ever unpicks
    /// the derivation, martens_own_binary_event_attribute_is_still_honored above starts failing —
    /// this says why, so the fix is to restore the base type rather than to re-add the second check.
    /// </summary>
    [Fact]
    public void martens_attribute_derives_from_the_promoted_one()
    {
        typeof(Marten.Events.BinaryEventAttribute)
            .IsAssignableTo(typeof(JasperFx.Events.BinaryEventAttribute))
            .ShouldBeTrue();

        // ...which is the whole mechanism: a lookup for the base finds the subclass.
        typeof(PromotionMartenMarked)
            .IsDefined(typeof(JasperFx.Events.BinaryEventAttribute), inherit: false)
            .ShouldBeTrue();
    }

    [Fact]
    public void an_unmarked_event_type_stays_on_the_json_path()
    {
        var options = new StoreOptions();
        options.Events.DefaultBinarySerializer = new CoreOnlyBinarySerializer();
        options.Events.AddEventType(typeof(PromotionUnmarked));

        options.EventGraph.EventMappingFor(typeof(PromotionUnmarked))
            .IsBinary.ShouldBeFalse();
    }

    /// <summary>
    /// The registration surface takes the core interface now, so a serializer that knows nothing
    /// about Marten can be registered. This would not have compiled before 9.26.
    /// </summary>
    [Fact]
    public void a_serializer_implementing_only_the_core_contract_can_be_registered()
    {
        var serializer = new CoreOnlyBinarySerializer();

        var options = new StoreOptions();
        options.Events.UseBinarySerializer<PromotionUnmarked>(serializer);

        options.EventGraph.EventMappingFor(typeof(PromotionUnmarked))
            .BinarySerializer.ShouldBeSameAs(serializer);
    }

    /// <summary>
    /// The other half of "non-breaking": a serializer written against the Marten-namespaced
    /// interface still satisfies the widened parameter, because that interface now derives from the
    /// core one and declares nothing itself.
    /// </summary>
    [Fact]
    public void a_serializer_implementing_the_marten_contract_still_registers()
    {
        var serializer = new LegacyMartenBinarySerializer();

        var options = new StoreOptions();
        options.Events.DefaultBinarySerializer = serializer;
        options.Events.UseBinarySerializer<PromotionUnmarked>(serializer);

        options.EventGraph.EventMappingFor(typeof(PromotionUnmarked))
            .BinarySerializer.ShouldBeSameAs(serializer);
    }

    /// <summary>
    /// Registration order must not matter. EventMapping used to resolve its binary serializer in
    /// its constructor, so AddEventType on a [BinaryEvent] type threw out of what looks like a
    /// plain type registration whenever DefaultBinarySerializer had not been assigned yet — while
    /// the two lines in the other order worked fine. Resolution is lazy now.
    /// </summary>
    [Fact]
    public void the_default_serializer_may_be_registered_after_the_event_type()
    {
        var options = new StoreOptions();

        Should.NotThrow(() => options.Events.AddEventType(typeof(PromotionCoreMarked)));

        options.Events.DefaultBinarySerializer = new CoreOnlyBinarySerializer();

        options.EventGraph.EventMappingFor(typeof(PromotionCoreMarked))
            .IsBinary.ShouldBeTrue();
    }

    /// <summary>
    /// Deferring the resolution must not lose the diagnostic — it moves to first use, which is
    /// where the documentation always said it was.
    /// </summary>
    [Fact]
    public void a_marked_event_type_with_no_serializer_anywhere_still_throws_on_use()
    {
        var options = new StoreOptions();
        options.Events.AddEventType(typeof(PromotionCoreMarked));

        var mapping = options.EventGraph.EventMappingFor(typeof(PromotionCoreMarked));

        Should.Throw<InvalidOperationException>(() => _ = mapping.BinarySerializer)
            .Message.ShouldContain("no IEventBinarySerializer was registered");
    }
}
