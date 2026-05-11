global using IStorageOperation = Marten.Internal.Operations.IStorageOperation;
// OperationRole consolidated to Weasel.Core per the dedup audit (#4350 / pillar #214).
// Marten previously declared its own byte-identical copy; this aliases all
// existing call sites to the canonical Weasel.Core enum without a code sweep.
global using OperationRole = Weasel.Core.OperationRole;
// SnapshotLifecycle consolidated to JasperFx.Events per the dedup audit
// (jasperfx#220 / pillar #214). Same pattern as OperationRole above.
global using SnapshotLifecycle = JasperFx.Events.Projections.SnapshotLifecycle;
