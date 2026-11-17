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

// jasperfx#756 (marten#5343) lifted the event exception types every store declared into JasperFx.Events,
// and marten#5847 (Marten 10) deleted Marten's own copies: Marten throws the JasperFx types directly, with
// no Marten subclass. These aliases keep the simple names resolving to the shared types in every file,
// including the ones that import Marten.Exceptions but not JasperFx.Events or JasperFx.
global using UnknownEventTypeException = JasperFx.Events.UnknownEventTypeException;
global using NonExistentStreamException = JasperFx.Events.NonExistentStreamException;
global using ExistingStreamIdCollisionException = JasperFx.Events.ExistingStreamIdCollisionException;
global using EventDeserializationFailureException = JasperFx.Events.EventDeserializationFailureException;
global using ArchivedStreamException = JasperFx.Events.ArchivedStreamException;
global using StreamLockedException = JasperFx.Events.StreamLockedException;
global using BadLinqExpressionException = JasperFx.BadLinqExpressionException;
// Still Marten's own type; JasperFx.Events declares one of the same name.
global using DefaultTenantUsageDisabledException = Marten.Exceptions.DefaultTenantUsageDisabledException;
