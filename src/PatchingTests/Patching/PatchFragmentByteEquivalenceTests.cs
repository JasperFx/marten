using System.Collections.Generic;
using System.Text;
using Marten;
using Marten.Patching;
using Marten.Services;
using Marten.Services.Json;
using Shouldly;
using Xunit;

namespace PatchingTests.Patching;

/// <summary>
/// Trust-gate for #4384's PatchFragment refactor: prove the new direct-UTF-8 emission
/// produces byte-identical output to the legacy <c>string + string.Replace</c> pipeline.
/// The patch fragment is the input to the PostgreSQL patch-doc SQL function, so the bytes
/// have to match exactly or the patch will mis-execute silently.
/// </summary>
public class PatchFragmentByteEquivalenceTests
{
    /// <summary>
    /// Sentinel used by the legacy <see cref="PatchFragment"/> to placeholder the polymorphic
    /// "value" key prior to a <c>string.Replace</c> substitution. Mirrored here so the test
    /// can compute the legacy bytes without depending on the production constant.
    /// </summary>
    private const string VALUE_LOOKUP = "___VALUE___";

    public static IEnumerable<object[]> Serializers()
    {
        yield return new object[] { new JsonNetSerializer() };
        yield return new object[] { new SystemTextJsonSerializer() };
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void no_value_patch_emits_clean_json_bytes(ISerializer serializer)
    {
        // Set / Increment / Duplicate / Remove patches — no "value" key. Legacy uses
        // ToCleanJson; new path uses WriteToCleanJson.
        var patch = new Dictionary<string, object>
        {
            ["type"] = "increment",
            ["increment"] = 7,
            ["path"] = "Count"
        };
        var legacy = legacyBytesFor(serializer, new List<PatchData> { new(patch, false) });
        var fresh = freshBytesFor(serializer, new List<PatchData> { new(patch, false) });

        fresh.ShouldBe(legacy);
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void value_patch_non_polymorphic_emits_spliced_bytes(ISerializer serializer)
    {
        // Set value patch — has "value" key with a concrete (non-polymorphic) type.
        // Legacy emits ToJson(copy) with VALUE_LOOKUP sentinel and string.Replace.
        // New emits the wrapper via WriteTo + splices in the value bytes (also WriteTo).
        var patch = new Dictionary<string, object>
        {
            ["type"] = "set",
            ["path"] = "Name",
            ["value"] = "Bob"
        };
        var legacy = legacyBytesFor(serializer, new List<PatchData> { new(patch, false) });
        var fresh = freshBytesFor(serializer, new List<PatchData> { new(patch, false) });

        fresh.ShouldBe(legacy);
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void value_patch_polymorphic_emits_with_types_bytes(ISerializer serializer)
    {
        // Append-element-polymorphic patch — "value" is a derived type stored under a
        // base-type collection slot. Legacy emits the value via ToJsonWithTypes (so the
        // $type metadata is present), then splices into the wrapper. New does the same
        // via WriteToJsonWithTypes for the value plus WriteTo for the wrapper.
        Animal value = new Dog { Name = "Rex", BarkVolume = 11 };
        var patch = new Dictionary<string, object>
        {
            ["type"] = "append",
            ["path"] = "Pets",
            ["value"] = value
        };
        var legacy = legacyBytesFor(serializer, new List<PatchData> { new(patch, true) });
        var fresh = freshBytesFor(serializer, new List<PatchData> { new(patch, true) });

        fresh.ShouldBe(legacy);
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void mixed_patch_set_emits_byte_identical_array(ISerializer serializer)
    {
        // The patch fragment is a JSON array of patches — exercising mixed entries
        // (no-value + non-polymorphic-value + polymorphic-value) catches comma-placement
        // regressions and ordering / boundary bugs that single-entry tests would miss.
        var noValue = new Dictionary<string, object>
        {
            ["type"] = "increment", ["increment"] = 1, ["path"] = "Count"
        };
        var valueSimple = new Dictionary<string, object>
        {
            ["type"] = "set", ["path"] = "Name", ["value"] = "Bob"
        };
        Animal poly = new Dog { Name = "Rex", BarkVolume = 11 };
        var valuePoly = new Dictionary<string, object>
        {
            ["type"] = "append", ["path"] = "Pets", ["value"] = poly
        };

        var patchSet = new List<PatchData>
        {
            new(noValue, false),
            new(valueSimple, false),
            new(valuePoly, true)
        };

        var legacy = legacyBytesFor(serializer, patchSet);
        var fresh = freshBytesFor(serializer, patchSet);

        fresh.ShouldBe(legacy);
    }

    /// <summary>
    /// Reproduce the legacy <c>PatchFragment.Apply</c> string-based emission inline and
    /// return its UTF-8 byte encoding. Mirrors the pre-#4384 code exactly so the test
    /// catches any drift between the legacy logic the issue body documents and the new
    /// direct-UTF-8 path.
    /// </summary>
    private static byte[] legacyBytesFor(ISerializer serializer, List<PatchData> patchSet)
    {
        var patchSetStr = new List<string>();
        foreach (var patch in patchSet)
        {
            var json = serializer.ToCleanJson(patch.Items);
            if (patch.Items.TryGetValue("value", out var document))
            {
                var value = patch.PossiblyPolymorphic
                    ? serializer.ToJsonWithTypes(document)
                    : serializer.ToJson(document);
                var copy = new Dictionary<string, object>();
                foreach (var item in patch.Items) copy.Add(item.Key, item.Value);
                copy["value"] = VALUE_LOOKUP;

                var patchJson = serializer.ToJson(copy);
                var replacedValue = patchJson.Replace($"\"{VALUE_LOOKUP}\"", value);

                json = replacedValue;
            }
            patchSetStr.Add(json);
        }

        return Encoding.UTF8.GetBytes("[" + string.Join(",", patchSetStr.ToArray()) + "]");
    }

    /// <summary>
    /// Drive the new <see cref="PatchFragment.writePatchArray"/> path. We construct
    /// PatchFragment with a null storage / session / function — none of those are read
    /// from <c>writePatchArray</c> (they live on the outer <c>Apply</c> SQL emission).
    /// </summary>
    private static byte[] freshBytesFor(ISerializer serializer, List<PatchData> patchSet)
    {
        var fragment = new PatchFragment(
            session: null,
            patchSet: patchSet,
            serializer: serializer,
            function: null,
            storage: null);

        using var buffer = new PooledByteBufferWriter(initialCapacity: 256);
        fragment.writePatchArray(buffer);
        return buffer.WrittenSpan.ToArray();
    }

    public abstract class Animal
    {
        public string Name { get; set; }
    }

    public class Dog : Animal
    {
        public int BarkVolume { get; set; }
    }
}
