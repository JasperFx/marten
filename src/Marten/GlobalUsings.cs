global using IStorageOperation = Marten.Internal.Operations.IStorageOperation;
// OperationRole consolidated to Weasel.Core per the dedup audit (#4350 / pillar #214).
// Marten previously declared its own byte-identical copy; this aliases all
// existing call sites to the canonical Weasel.Core enum without a code sweep.
global using OperationRole = Weasel.Core.OperationRole;
// SnapshotLifecycle consolidated to JasperFx.Events per the dedup audit
// (jasperfx#220 / pillar #214). Same pattern as OperationRole above.
global using SnapshotLifecycle = JasperFx.Events.Projections.SnapshotLifecycle;
// TenancyStyle + DeleteStyle consolidated to JasperFx per the dedup audit
// (jasperfx#327 / marten#4517 / pillar #214). Ordinals unchanged
// (Single=0/Conjoined=1, Remove=0/SoftDelete=1). Same alias pattern as above.
global using TenancyStyle = JasperFx.MultiTenancy.TenancyStyle;
global using DeleteStyle = JasperFx.DeleteStyle;
// Metadata markers consolidated to JasperFx.Metadata per the dedup audit
// (jasperfx#330 / marten#4520 / pillar #214). Shapes are byte-identical
// (ITracked uses Marten's non-nullable string). Same alias pattern as above.
global using ISoftDeleted = JasperFx.Metadata.ISoftDeleted;
global using IVersioned = JasperFx.Metadata.IVersioned;
global using ITracked = JasperFx.Metadata.ITracked;
// Patching surface consolidated to JasperFx.Events per the dedup audit
// (jasperfx#331 / marten#4521 / pillar #214). RemoveAction (enum) is aliased here;
// IPatchExpression<T> is an open generic so it cannot be aliased — its consumer
// files import JasperFx.Events directly.
global using RemoveAction = JasperFx.Events.RemoveAction;
