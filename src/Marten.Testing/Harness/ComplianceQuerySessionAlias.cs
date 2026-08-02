// The shared compliance suites declare self-aggregating types whose EvolveAsync convention method
// takes the store's own read session. JasperFx's aggregate source generator resolves the parameter
// by type name, so a per-consumer global alias lets one shared source file bind to Marten's
// IQuerySession here and to Polecat's in Polecat.
global using ComplianceQuerySession = Marten.IQuerySession;

// Same mechanism for the EventProjection suites. Those declare projection types at file scope, so
// they cannot reach the <TOperations, TQuerySession> pair their suite class is generic over.
global using ComplianceOperations = Marten.IDocumentOperations;
global using ComplianceEventProjection = Marten.Events.Projections.EventProjection;
