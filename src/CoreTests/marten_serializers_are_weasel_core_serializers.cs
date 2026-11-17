using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marten;
using Marten.Services;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5733. Marten's <c>ISerializer</c> extends the Critter Stack's shared <c>Weasel.Core.ISerializer</c>,
/// so either of Marten's serializers can be handed to code written against that contract, and both
/// gain its string <c>FromJson</c> overloads. Constructs the serializers directly, so it asserts both
/// whatever <c>DEFAULT_SERIALIZER</c> is.
/// </summary>
public class marten_serializers_are_weasel_core_serializers
{
    public class Target
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public int Number { get; set; }
    }

    public static IEnumerable<object[]> Serializers()
    {
        yield return [new SystemTextJsonSerializer()];
        yield return [new JsonNetSerializer()];
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void round_trips_through_the_weasel_core_contract(Marten.ISerializer serializer)
    {
        Weasel.Core.ISerializer shared = serializer;
        var target = new Target { Id = Guid.NewGuid(), Name = "shared", Number = 3 };

        var json = shared.ToJson(target);

        var generic = shared.FromJson<Target>(json);
        generic.Id.ShouldBe(target.Id);
        generic.Name.ShouldBe("shared");
        generic.Number.ShouldBe(3);

        var untyped = (Target)shared.FromJson(typeof(Target), json);
        untyped.Id.ShouldBe(target.Id);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        shared.FromJson<Target>(stream).Name.ShouldBe("shared");
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void enum_storage_and_casing_read_the_same_through_either_contract(Marten.ISerializer serializer)
    {
        Weasel.Core.ISerializer shared = serializer;

        shared.EnumStorage.ShouldBe(serializer.EnumStorage);
        shared.Casing.ShouldBe(serializer.Casing);
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void string_reads_use_marten_deserialization_options(Marten.ISerializer serializer)
    {
        // A Marten serializer reads an untyped value the same way through the string overload as
        // through the stream overload it is defaulted onto, rather than a separate options set.
        var json = "{\"Id\":\"" + Guid.NewGuid() + "\",\"Name\":\"x\",\"Number\":1}";

        var viaString = serializer.FromJson<Dictionary<string, object>>(json);
        var viaStream = serializer.FromJson<Dictionary<string, object>>(
            new MemoryStream(Encoding.UTF8.GetBytes(json)));

        viaString["Name"].GetType().ShouldBe(viaStream["Name"].GetType());
        viaString["Number"].GetType().ShouldBe(viaStream["Number"].GetType());
    }
}
