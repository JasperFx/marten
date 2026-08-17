using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 9 -- binary event serialization compliance, shipped in JasperFx 2.50.0 alongside the
 * promotion of IEventBinarySerializer and BinaryEventAttribute out of Marten.Events and into
 * JasperFx.Events (jasperfx#669).
 *
 * Marten is the reference implementation here rather than a late adopter: the feature originated as
 * marten#4515 and the suite was written against it, so Polecat (polecat#475) and Fisher (fisher#93)
 * are being ported to match this behavior. That makes a red fact in this suite genuinely
 * interesting -- it is either a real Marten gap or the shared suite encoding something Marten never
 * promised.
 *
 * It is an opt-in suite: a store that has not implemented binary event storage simply does not
 * enroll, and IComplianceStoreRegistrar.UseBinarySerializer / SetDefaultBinarySerializer keep their
 * throwing defaults. Marten implements both on MartenComplianceRegistrar, which is only possible
 * because EventGraph.UseBinarySerializer / DefaultBinarySerializer were widened in 9.26 to accept
 * the promoted JasperFx.Events.IEventBinarySerializer.
 *
 * One real defect fell out of enrolling it, and it was never suite-specific. The suite's config
 * registers its event types before it sets the store-wide default serializer, and EventMapping used
 * to resolve its binary serializer in its constructor -- so AddEventType<T>() on a [BinaryEvent]
 * type threw "no IEventBinarySerializer was registered" purely because of the order two lines were
 * written in. Resolution is lazy now (EventMapping.BinarySerializer), which is what the
 * documentation always described.
 */

public class binary_event_serialization_compliance
    : BinaryEventSerializationCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
