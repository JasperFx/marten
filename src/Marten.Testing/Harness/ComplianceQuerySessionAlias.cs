// The shared compliance suites declare self-aggregating types whose EvolveAsync convention method
// takes the store's own read session. JasperFx's aggregate source generator resolves the parameter
// by type name, so a per-consumer global alias lets one shared source file bind to Marten's
// IQuerySession here and to Polecat's in Polecat.
global using ComplianceQuerySession = Marten.IQuerySession;
