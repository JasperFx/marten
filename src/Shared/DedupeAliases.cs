// Centralized global-using aliases for the Critter Stack 2026 dedupe pillar
// (jasperfx#214). Marten previously declared its own copies of these types; as
// each is lifted into JasperFx / JasperFx.Events / Weasel.Core, Marten consumes
// the canonical type and aliases the old unqualified name here so existing call
// sites across Marten + its test/extension projects keep compiling without a
// per-file sweep.
//
// This file is linked into every JasperFx-referencing project via
// Directory.Build.props (ImplicitUsings is off in this repo, so MSBuild
// <Using> items don't apply — a linked C# global-using file is used instead).

global using IStorageOperation = Marten.Internal.Operations.IStorageOperation;
// #4821: the dialect-neutral closed-shape storage contracts (IStorageSession, IStorageDatabase,
// IProviderGraph, IDocumentStorage family, DocumentProvider<T>, IChangeTracker, IDuplicatedField,
// ConcurrencyChecks, IDeletion, NoDataReturnedCall, IOperationFragment, ISelector, and the neutral
// IStorageOperation/ISelectClause bases) moved to the shared Weasel.Storage library. The simple
// names keep resolving everywhere via this namespace import; the Marten-side derived interfaces
// that keep the same simple names are pinned by aliases (IStorageOperation above, ISelectClause below).
global using Weasel.Storage;
// Marten's LINQ select clause derives from the neutral Weasel.Storage.ISelectClause (#4821);
// unqualified usage keeps meaning the Marten interface.
global using ISelectClause = Marten.Linq.SqlGeneration.ISelectClause;
// OperationRole -> Weasel.Core (#4350 / pillar #214)
global using OperationRole = Weasel.Core.OperationRole;
// SnapshotLifecycle -> JasperFx.Events (jasperfx#220 / pillar #214)
global using SnapshotLifecycle = JasperFx.Events.Projections.SnapshotLifecycle;
// TenancyStyle + DeleteStyle -> JasperFx (jasperfx#327 / marten#4517)
global using TenancyStyle = JasperFx.MultiTenancy.TenancyStyle;
global using DeleteStyle = JasperFx.DeleteStyle;
// IRevisioned reverted to JasperFx's canonical int signature; ILongVersioned (long)
// added for MultiStreamProjection-derived docs whose Version is the event sequence
// (jasperfx#348 / marten#4526 / marten#4528).
global using IRevisioned = JasperFx.IRevisioned;
global using ILongVersioned = JasperFx.ILongVersioned;
// Metadata markers -> JasperFx.Metadata (jasperfx#330 / marten#4520)
global using ISoftDeleted = JasperFx.Metadata.ISoftDeleted;
global using IVersioned = JasperFx.Metadata.IVersioned;
global using ITracked = JasperFx.Metadata.ITracked;
// Patching -> JasperFx.Events (jasperfx#331 / marten#4521); IPatchExpression<T>
// is an open generic and cannot be aliased.
global using RemoveAction = JasperFx.Events.RemoveAction;
// IDocumentSchemaResolver -> JasperFx.Events (jasperfx#333 / marten#4523)
global using IDocumentSchemaResolver = JasperFx.Events.IDocumentSchemaResolver;
// TrackLevel -> JasperFx.OpenTelemetry (jasperfx#332 / marten#4522)
global using TrackLevel = JasperFx.OpenTelemetry.TrackLevel;
// DcbConcurrencyException -> JasperFx.Events (jasperfx#328 / marten#4518)
global using DcbConcurrencyException = JasperFx.Events.DcbConcurrencyException;
// IdentityAttribute -> JasperFx (jasperfx#335 / marten#4525). Was an empty
// MartenAttribute marker; only consumed via HasAttribute<IdentityAttribute>().
global using IdentityAttribute = JasperFx.IdentityAttribute;
// Serialization enums -> Weasel.Core (weasel#287 / marten#4527). Members unchanged.
global using Casing = Weasel.Core.Casing;
global using NonPublicMembersStorage = Weasel.Core.NonPublicMembersStorage;
// Hi-Lo sequence pieces -> Weasel.Core.Sequences (weasel#287 / marten#4527).
// Shapes are identical; HiloSequence derives from HiloSequenceBase.
global using ISequence = Weasel.Core.Sequences.ISequence;
global using IReadOnlyHiloSettings = Weasel.Core.Sequences.IReadOnlyHiloSettings;
global using HiloSettings = Weasel.Core.Sequences.HiloSettings;
global using HiloSequenceAdvanceToNextHiAttemptsExceededException = Weasel.Core.Sequences.HiloSequenceAdvanceToNextHiAttemptsExceededException;
