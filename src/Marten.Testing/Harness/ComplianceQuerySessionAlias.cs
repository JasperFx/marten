// The shared compliance suites declare self-aggregating types whose EvolveAsync convention method
// takes the store's own read session. JasperFx's aggregate source generator resolves the parameter
// by type name, so a per-consumer global alias lets one shared source file bind to Marten's
// IQuerySession here and to Polecat's in Polecat.
global using ComplianceQuerySession = Marten.IQuerySession;

// Same mechanism for the EventProjection suites. Those declare projection types at file scope, so
// they cannot reach the <TOperations, TQuerySession> pair their suite class is generic over.
global using ComplianceOperations = Marten.IDocumentOperations;
global using ComplianceEventProjection = Marten.Events.Projections.EventProjection;

// The string stream identity suite (JasperFx compliance wave 3) declares a custom single stream
// projection at file scope, so it cannot reach its suite class's <TOperations, TQuerySession>
// generics either. Each product subclasses JasperFxSingleStreamProjectionBase under its own name,
// so the base type comes in through this alias.
global using ComplianceStringPartyProjectionBase =
    Marten.Events.Aggregation.SingleStreamProjection<JasperFx.Events.ComplianceTests.StringQuestParty, string>;

// The multi-stream projection suite declares its projection at file scope for the same reason, and
// its base is generic over both the document and the identity, so this alias names a closed generic.
global using ComplianceMultiStreamProjectionBase =
    Marten.Events.Projections.MultiStreamProjection<
        JasperFx.Events.ComplianceTests.ComplianceDepartment, string>;

// jasperfx#754 (marten#5343). AggregateToManyCompliance's whole point is that the operator drives
// the REGISTERED projection -- identity routing, a session-reading custom grouper, ShouldDelete --
// rather than reimplementing the fold inline, so its two multi-stream projections are real
// registrations declared at file scope, and need the same closed-generic alias as the department
// projection above.
global using ComplianceBalanceProjectionBase =
    Marten.Events.Projections.MultiStreamProjection<
        JasperFx.Events.ComplianceTests.ComplianceBalance, System.Guid>;
global using ComplianceMemberLoyaltyProjectionBase =
    Marten.Events.Projections.MultiStreamProjection<
        JasperFx.Events.ComplianceTests.ComplianceMemberLoyalty, System.Guid>;

// jasperfx#763 (marten#5343). ProjectionSideEffectCompliance's projection has to OVERRIDE
// RaiseSideEffects, so it must derive from Marten's own SingleStreamProjection<TDoc, TId> rather
// than anything the suite could name itself.
global using ComplianceWatchtowerProjectionBase =
    Marten.Events.Aggregation.SingleStreamProjection<
        JasperFx.Events.ComplianceTests.ComplianceWatchtower, System.Guid>;
