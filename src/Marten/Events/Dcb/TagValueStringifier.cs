using System;
using System.Globalization;

namespace Marten.Events.Dcb;

/// <summary>
/// Converts a tag value (the primitive returned by
/// <see cref="JasperFx.Events.Tags.ITagTypeRegistration.ExtractValue"/>) to its
/// canonical string form for storage in the heterogeneous
/// <c>mt_dcb_tag_version</c> side table. The DCB tag-version side table is keyed
/// across every registered tag type, so values are stringified to text rather
/// than stored in their native types.
/// </summary>
/// <remarks>
/// Stable formatting matters: the same tag value must produce the same string
/// across processes and across .NET runtimes so concurrent appenders agree on
/// which row to lock.
/// <list type="bullet">
///   <item><description><see cref="Guid"/>: "d" (lowercase, hyphenated) format.</description></item>
///   <item><description><see cref="string"/>: passed through.</description></item>
///   <item><description>Numeric primitives: <see cref="CultureInfo.InvariantCulture"/>.</description></item>
/// </list>
/// </remarks>
internal static class TagValueStringifier
{
    public static string Stringify(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            string s => s,
            Guid g => g.ToString("d"),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
