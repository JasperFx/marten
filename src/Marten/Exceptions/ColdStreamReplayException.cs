#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Marten.Exceptions;

/// <summary>
///     A projection was about to replay event history (a rebuild, or a first start from the beginning) while
///     streams have a cold part, which the async daemon does not read. Nothing was torn down or projected. Opt
///     a projection that does not need the cold part out with <c>Options.IgnoreColdStreams()</c>.
/// </summary>
[Experimental(Marten.Events.Cold.ColdEventStorage.DiagnosticId, UrlFormat = Marten.Events.Cold.ColdEventStorage.DocumentationUrl)]
public class ColdStreamReplayException: MartenException
{
    internal ColdStreamReplayException(string projectionName, string databaseIdentifier, string? tenantId): base(
        $"Projection '{projectionName}' would replay the events of database '{databaseIdentifier}'" +
        (tenantId == null ? "" : $" for tenant '{tenantId}'") +
        " from the beginning, but streams there have a cold part that the async daemon does not read, so its read models would silently miss those events." +
        " If the projection does not need the cold part, opt in with Options.IgnoreColdStreams() (on a composite, on the composite's own options).")
    {
        ProjectionName = projectionName;
        DatabaseIdentifier = databaseIdentifier;
        TenantId = tenantId;
    }

    public string ProjectionName { get; }

    public string DatabaseIdentifier { get; }

    public string? TenantId { get; }
}
