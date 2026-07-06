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
