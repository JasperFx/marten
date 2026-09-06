// #4821 Story 6 / weasel#327: the shared Weasel.Core SQL-generation contract introduced
// Weasel.Core.ICommandBuilder + Weasel.Core.SqlGeneration.ISqlFragment, which now collide by
// simple name with the Weasel.Postgresql equivalents in the many files that import both
// namespaces. Marten's SQL-generation implementers target the Postgres-typed contracts (the
// dialect ISqlFragment forwards the neutral Apply via a default interface method), so alias the
// simple names to the Postgresql types assembly-wide. Movable code that should target the neutral
// Weasel.Core contract fully-qualifies it, which overrides these aliases.
global using ICommandBuilder = Weasel.Postgresql.ICommandBuilder;
global using ISqlFragment = Weasel.Postgresql.SqlGeneration.ISqlFragment;
global using ICompoundFragment = Weasel.Postgresql.SqlGeneration.ICompoundFragment;

// jasperfx#756 (marten#5343): JasperFx.Events 2.64.0 adds canonical versions of the six event
// exception types that until now were declared once per store, so every file importing both
// JasperFx.Events and Marten.Exceptions sees an ambiguous simple name. Marten's own copies are
// public API that callers catch by name, and the lift commit is explicit that "stores will
// subclass or type-forward in a later node" -- so the resolution here is deliberately the
// API-preserving one: alias the simple names to Marten's types assembly-wide, exactly as the
// Weasel aliases above do, and leave the consolidation to the node that owns it. Code that wants
// the shared type fully-qualifies it, which overrides these aliases.
global using UnknownEventTypeException = Marten.Exceptions.UnknownEventTypeException;
global using NonExistentStreamException = Marten.Exceptions.NonExistentStreamException;
global using ExistingStreamIdCollisionException = Marten.Exceptions.ExistingStreamIdCollisionException;
global using EventDeserializationFailureException = Marten.Exceptions.EventDeserializationFailureException;
global using StreamLockedException = Marten.Exceptions.StreamLockedException;
global using DefaultTenantUsageDisabledException = Marten.Exceptions.DefaultTenantUsageDisabledException;

