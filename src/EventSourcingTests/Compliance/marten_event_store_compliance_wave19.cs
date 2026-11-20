using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 19 -- parking the failing slice (JasperFx 3.10, marten#6003).
 */

/// <summary>
///     marten#6003. A failing identity of a single-stream projection parks in <c>mt_parked_identities</c>, in the
///     batch that moves the shard's progression, and a replay, a discard or a per-identity rebuild unparks it.
/// </summary>
public class parked_identity_compliance
    : ParkedIdentityCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     marten#6003. A failing identity of a multi-stream projection parks when its events can be found again: by
///     stream, by DCB tag (the daemon reads the tags of a projection that groups by them), or through the
///     projection's slice index table.
/// </summary>
public class multi_stream_parked_identity_compliance
    : MultiStreamParkedIdentityCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
