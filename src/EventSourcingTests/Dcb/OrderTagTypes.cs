#nullable enable
using System;

namespace EventSourcingTests.Dcb;

/// <summary>
/// The retroactive-tagging fixture types. The portable AssignTagWhere behavior now lives in
/// <see cref="JasperFx.Events.ComplianceTests.AssignTagWhereCompliance{TFixture,TOperations,TQuerySession}"/>;
/// these records stay here because Marten's HStore-specific
/// <see cref="hstore_assign_tag_where_tests"/> exercises the same domain against a storage mode
/// Marten alone supports.
/// </summary>
public record RegionId(Guid Value);

public record OrderPlaced(string OrderNumber, decimal Amount);

public record OrderShipped(string OrderNumber);

public record OrderCancelled(string OrderNumber, string Reason);
