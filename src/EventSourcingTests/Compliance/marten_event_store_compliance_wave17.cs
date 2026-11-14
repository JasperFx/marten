using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 17 -- the JasperFx 3.0 lifts Marten adopts (marten#5732).
 */

/// <summary>
///     jasperfx#1049. The three step-through members on IEventStore forward to the shared
///     ProjectionStepThrough, over Marten's own serializer: every recorded step is a detached copy, and a
///     replay folds through the projection's real dispatch rather than reflecting on Apply methods.
/// </summary>
public class projection_step_through_compliance
    : ProjectionStepThroughCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
