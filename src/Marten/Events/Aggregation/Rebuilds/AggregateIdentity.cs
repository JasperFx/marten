using System;

namespace Marten.Events.Aggregation.Rebuilds;

internal record AggregateIdentity(long Number, Guid Id, string Key);
