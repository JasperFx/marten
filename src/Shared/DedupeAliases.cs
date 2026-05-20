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
// OperationRole -> Weasel.Core (#4350 / pillar #214)
global using OperationRole = Weasel.Core.OperationRole;
// SnapshotLifecycle -> JasperFx.Events (jasperfx#220 / pillar #214)
global using SnapshotLifecycle = JasperFx.Events.Projections.SnapshotLifecycle;
// TenancyStyle + DeleteStyle -> JasperFx (jasperfx#327 / marten#4517)
global using TenancyStyle = JasperFx.MultiTenancy.TenancyStyle;
global using DeleteStyle = JasperFx.DeleteStyle;
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
