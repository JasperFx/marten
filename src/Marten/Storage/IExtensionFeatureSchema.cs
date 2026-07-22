using Weasel.Core.Migrations;

namespace Marten.Storage;

/// <summary>
/// Marker for an <see cref="IFeatureSchema"/> contributed by an opt-in Marten extension whose code has been
/// folded into the core Marten assembly (e.g. TimescaleDB, #4980) yet is still registered via
/// <c>StorageFeatures.Add()</c>. <see cref="StorageFeatures.AllActiveFeatures"/> identifies "custom"
/// (extension / user) features by their assembly differing from Marten's; an in-assembly extension feature
/// carries this marker so it is still yielded — after the document and event tables — instead of being
/// silently dropped by that assembly heuristic.
/// </summary>
internal interface IExtensionFeatureSchema : IFeatureSchema
{
}
