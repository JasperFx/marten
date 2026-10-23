using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 15 (#5740) -- the JasperFx 2.93.0 compliance wave. The document half of the same wave
 * (SoftDeleteCompliance, DocumentMetadataCompliance) is enrolled beside the other document suites in
 * marten_document_storage_compliance.cs.
 */

/// <summary>
///     jasperfx#809. The persisted projection start floor on MartenDatabase, over
///     mt_event_projection_floors: absent until stored, insert-if-absent returning the stored winner,
///     the operator's set and clear, one floor per tenant, and a floor that outlives cleaning the
///     event data.
/// </summary>
public class projection_floor_compliance
    : ProjectionFloorCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
