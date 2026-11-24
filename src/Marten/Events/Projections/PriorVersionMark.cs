using System.Collections;
using System.Linq;
using System.Reflection;
using JasperFx.Events.Projections;

namespace Marten.Events.Projections;

/// <summary>
/// #6186: whether <see cref="AsyncOptions.SubscribeFromPriorVersionMark" /> was called on a projection's options.
/// </summary>
/// <remarks>
/// JasperFx.Events keeps the configured starting positions in a private list of internal strategy types and has no
/// public way to ask for them, so this reads that list reflectively. If a later JasperFx.Events changes its shape,
/// the lookup finds nothing and answers false: the startup check stops refusing, and
/// <c>refusing_subscribe_from_prior_version_mark</c> in EventSourcingTests goes red to say so. It never throws.
/// Replace it with a public member once JasperFx.Events has one.
/// </remarks>
internal static class PriorVersionMark
{
    private const string StrategyTypeName = "FromPriorVersionMark";

    private static readonly FieldInfo? _strategies =
        typeof(AsyncOptions).GetField("_strategies", BindingFlags.NonPublic | BindingFlags.Instance);

    public static bool IsConfiguredOn(AsyncOptions? options)
    {
        if (options == null || _strategies?.GetValue(options) is not IEnumerable strategies) return false;

        return strategies.Cast<object>().Any(x => x?.GetType().Name == StrategyTypeName);
    }
}
