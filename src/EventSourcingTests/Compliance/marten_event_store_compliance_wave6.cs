using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 6 enrollments. Same shape as the earlier enrollment files -- empty subclasses closing the
 * shared suites over Marten's session pair.
 *
 * StrongTypedIdentityCompliance shipped in JasperFx 2.42.0 (jasperfx#636) alongside the
 * ComplianceStoreConfig.RegisterValueType<T>() seam member that MartenComplianceFixture already
 * implements. It arrived in the package with the 2.42.2 bump but was never enrolled, so these tests
 * were shipping unrun on this store.
 */

public class strong_typed_identity_compliance
    : StrongTypedIdentityCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
