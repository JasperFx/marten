using System;
using System.Collections.Generic;

namespace Marten.Events.Projections;

/// <summary>
///     A projection that may declare backfills: which schema objects they need, and which backfill rows a
///     shard of it must have completed before its data is current.
/// </summary>
internal interface IProjectionWithBackfills
{
    string Name { get; }

    uint Version { get; }

    bool HasBackfills { get; }

    IReadOnlyList<string> BackfillNames { get; }

    Type BackfilledDocumentType { get; }
}
