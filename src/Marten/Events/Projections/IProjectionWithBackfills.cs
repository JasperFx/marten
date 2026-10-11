using System;

namespace Marten.Events.Projections;

/// <summary>
///     Decides which backfill schema objects the store needs.
/// </summary>
internal interface IProjectionWithBackfills
{
    bool HasBackfills { get; }

    Type BackfilledDocumentType { get; }
}
