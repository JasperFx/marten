#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using JasperFx;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql.Tables;

namespace Marten.Storage;

/// <summary>
///     #5969: brings one feature's schema objects up to date with nothing but synchronous database calls, and
///     without needing any other thread to make progress.
/// </summary>
/// <remarks>
///     <para>
///     This exists for the one piece of storage that a synchronous caller has to have before it can return:
///     the HiLo sequences behind <c>session.Store()</c>, which assigns a numeric id the moment a document is
///     stored. Weasel only migrates asynchronously, and blocking a thread-pool thread on that asynchronous work
///     (while every other caller blocked on the lock around it) starved the pool: the work needed a free pool
///     thread to finish, and every one of them was waiting on it.
///     </para>
///     <para>
///     Every command here holds exactly one SQL statement. That is not a style choice. For a synchronous call,
///     Npgsql writes the second and later statements of a multi-statement command or batch on the thread pool
///     (to keep a large batch from deadlocking against the server's results), so a synchronous multi-statement
///     command waits on a pool thread just as surely as sync-over-async does. One statement per command is
///     written and read entirely on the calling thread. DDL that Weasel writes as several statements runs in
///     one explicit transaction, which is the same all-or-nothing behaviour as the implicit transaction a
///     multi-statement command gets.
///     </para>
///     <para>
///     It is a synchronous port of the path Weasel takes for a single feature, using Weasel's own pieces for
///     everything that decides what to do: each object's introspection query, its delta reader (run over a
///     <see cref="BufferedSchemaReader" />, so it completes without any I/O), <see cref="SchemaMigration" />
///     and its <see cref="SchemaMigration.AssertPatchingIsValid" /> check, and the migrator's DDL writers.
///     Only the I/O around them is synchronous here. Deliberately left out: the database initializers (the
///     tenant partition registry, which concerns document tables only) and Weasel's in-process migration
///     lock, which is asynchronous and private. The objects built here are never built by any other lazy
///     path, and their builds already run one at a time under the caller's lock, so what is left to race is
///     another process or an explicit apply, exactly as before. The schema statement guards itself against
///     that, and the statements get the same retry on catalog-concurrency errors as Weasel's.
///     </para>
/// </remarks>
internal static class SynchronousFeatureMigration
{
    private const int MaxConcurrencyRetries = 3;

    private static long _asynchronousDeltaReads;

    /// <summary>
    ///     How many delta reads did not complete synchronously over the buffered reader and had to be waited
    ///     on. Always zero unless a Weasel change has made a delta reader do I/O or yield of its own; a test
    ///     holds it there.
    /// </summary>
    internal static long AsynchronousDeltaReads => Interlocked.Read(ref _asynchronousDeltaReads);

    /// <summary>
    ///     Create or update <paramref name="feature" />'s objects as <paramref name="autoCreate" /> allows.
    ///     With <see cref="AutoCreate.None" /> this does nothing and never touches the database.
    /// </summary>
    public static void Apply(
        Func<NpgsqlConnection> connectionSource,
        Migrator migrator,
        AutoCreate autoCreate,
        IMigrationLogger? logger,
        IFeatureSchema feature)
    {
        if (autoCreate == AutoCreate.None)
        {
            // The asynchronous path read the catalog here and then applied nothing. Nothing would change
            // either way, so this skips the round trip
            return;
        }

        var objects = feature.Objects;
        if (objects.Length == 0)
        {
            return;
        }

        assertValidIdentifiers(migrator, objects);

        using var conn = connectionSource();
        conn.Open();

        var migration = Determine(conn, migrator, objects);
        if (migration.Difference == SchemaPatchDifference.None)
        {
            return;
        }

        migration.AssertPatchingIsValid(autoCreate);

        applyAll(conn, migration, migrator, autoCreate, logger ?? new DefaultMigrationLogger());

        // As MartenMigrator does on the async path: a type an extension adds is unknown to the data source until
        // its types are reloaded
        if (MartenMigrator.CreatesAnExtension(migration))
        {
            conn.ReloadTypes();
        }
    }

    /// <summary>
    ///     The synchronous counterpart of <c>SchemaMigration.DetermineAsync</c> for a handful of objects: each
    ///     object's introspection queries, one statement per command, read synchronously and in full, then
    ///     Weasel's delta readers over the result sets in order.
    /// </summary>
    internal static SchemaMigration Determine(NpgsqlConnection conn, Migrator migrator, ISchemaObject[] objects)
    {
        var builder = migrator.CreateCommandBuilder(conn);
        for (var i = 0; i < objects.Length; i++)
        {
            if (i > 0) builder.StartNewCommand();
            objects[i].ConfigureQueryCommand(builder);
        }

        var batch = builder.Compile();
        var results = new List<BufferedSchemaReader.ResultSet>();

        foreach (var statement in SplitStatements(batch.CommandText))
        {
            using var command = conn.CreateCommand();
            command.CommandText = statement;
            foreach (var parameter in batch.Parameters.OfType<NpgsqlParameter>())
            {
                if (references(statement, parameter.ParameterName))
                {
                    command.Parameters.Add(parameter.Clone());
                }
            }

            using var reader = command.ExecuteReader();
            BufferedSchemaReader.Capture(reader, results);
        }

        var deltas = new List<ISchemaObjectDelta>(objects.Length);
        using (var buffered = BufferedSchemaReader.Over(results))
        {
            deltas.Add(readDelta(objects[0], buffered));
            for (var i = 1; i < objects.Length; i++)
            {
                buffered.NextResult();
                deltas.Add(readDelta(objects[i], buffered));
            }
        }

        foreach (var postProcessing in deltas.OfType<ISchemaObjectDeltaWithPostProcessing>().ToArray())
        {
            postProcessing.PostProcess(deltas);
        }

        return new SchemaMigration(deltas);
    }

    private static ISchemaObjectDelta readDelta(ISchemaObject schemaObject, BufferedSchemaReader reader)
    {
        var task = schemaObject.CreateDeltaAsync(reader, CancellationToken.None);

        if (!task.IsCompleted)
        {
            // Not expected: everything the reader is asked for is already in memory. Waiting is still correct,
            // just no longer free of the thread pool, so count it rather than fail the caller's Store()
            Interlocked.Increment(ref _asynchronousDeltaReads);
        }

#pragma warning disable VSTHRD002
        return task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    private static void assertValidIdentifiers(Migrator migrator, IEnumerable<ISchemaObject> objects)
    {
        foreach (var schemaObject in objects)
        {
            foreach (var objectName in schemaObject.AllNames())
            {
                migrator.AssertValidIdentifier(objectName.Name);
            }

            if (schemaObject is ISchemaObjectWithLocalIdentifiers withLocals)
            {
                foreach (var name in withLocals.LocalIdentifiers())
                {
                    migrator.AssertValidLocalIdentifier(name);
                }
            }
        }
    }

    /// <summary>
    ///     The synchronous counterpart of <c>Migrator.ApplyAllAsync</c> and the PostgreSQL migrator's
    ///     <c>executeDelta</c>: the same warnings, the same DDL in the same chunks, with the same retry on
    ///     catalog-concurrency errors and the same failure handling.
    /// </summary>
    private static void applyAll(
        NpgsqlConnection conn,
        SchemaMigration migration,
        Migrator migrator,
        AutoCreate autoCreate,
        IMigrationLogger logger)
    {
        if (!migration.Deltas.Any())
        {
            return;
        }

        if (!migrator.RefuseDestructiveChanges)
        {
            foreach (var delta in migration.Deltas)
            {
                if (DestructiveChange.DropsAndRecreates(delta))
                {
                    logger.DestructiveChange(DestructiveChange.Describe(delta));
                }
            }
        }

        foreach (var delta in migration.Deltas)
        {
            if (delta is ISchemaObjectDeltaWithWithheldDrops { WithheldDrops.Count: > 0 } withholding)
            {
                logger.WithheldDrop(
                    AddOnlyMigration.Describe(delta.SchemaObject.Identifier, withholding.WithheldDrops));
            }
        }

        var writer = new StringWriter();
        if (migration.Schemas.Any())
        {
            migrator.WriteSchemaCreationSql(migration.Schemas, writer);
        }

        migration.WriteAllUpdates(writer, migrator, autoCreate);

        // The same chunks Weasel executes as one command each
        var chunks = writer
            .ToString()
            .Split(new[] { IndexDefinition.IndexCreationBeginComment, IndexDefinition.IndexCreationEndComment },
                StringSplitOptions.RemoveEmptyEntries);

        foreach (var sql in chunks)
        {
            var statements = SplitStatements(sql);
            if (statements.Count == 0)
            {
                continue;
            }

            logger.SchemaChange(sql);

            try
            {
                executeWithConcurrencyRetry(conn, statements);
            }
            catch (Exception e)
            {
                var failure = migrator.IsInsufficientPrivilege(e)
                    ? new InsufficientDatabasePrivilegeException(migrator.RoleFor(conn), conn.Database, sql, e)
                    : e;

                if (logger is DefaultMigrationLogger)
                {
                    if (ReferenceEquals(failure, e))
                    {
                        throw;
                    }

                    throw failure;
                }

                using var reported = conn.CreateCommand();
                reported.CommandText = sql;
                logger.OnFailure(reported, failure);
            }
        }
    }

    private static void executeWithConcurrencyRetry(NpgsqlConnection conn, IReadOnlyList<string> statements)
    {
        for (var attempt = 1;; attempt++)
        {
            try
            {
                executeAtomically(conn, statements);
                return;
            }
            catch (PostgresException e) when (attempt < MaxConcurrencyRetries && isTransientCatalogConcurrency(e))
            {
                // Two sessions changing the catalog at once, typically another process or another feature
                // being created beside this one. The transaction rolled back, and every statement Weasel
                // writes for this is idempotent anyway. A synchronous wait here holds one thread for a
                // moment, but needs no other thread to end
                Thread.Sleep(50 * attempt + Random.Shared.Next(0, 50));

                if (conn.State == ConnectionState.Broken)
                {
                    conn.Close();
                }

                if (conn.State != ConnectionState.Open)
                {
                    conn.Open();
                }
            }
        }
    }

    private static void executeAtomically(NpgsqlConnection conn, IReadOnlyList<string> statements)
    {
        if (statements.Count == 1)
        {
            using var single = conn.CreateCommand();
            single.CommandText = statements[0];
            single.ExecuteNonQuery();
            return;
        }

        using var tx = conn.BeginTransaction();
        foreach (var statement in statements)
        {
            using var command = conn.CreateCommand();
            command.Transaction = tx;
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static bool isTransientCatalogConcurrency(PostgresException e)
    {
        return e.SqlState switch
        {
            PostgresErrorCodes.SerializationFailure => true,
            PostgresErrorCodes.DeadlockDetected => true,
            PostgresErrorCodes.InternalError =>
                e.MessageText.Contains("tuple concurrently updated", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>
    ///     Does <paramref name="statement" /> use the named parameter, as <c>:name</c> or <c>@name</c>?
    /// </summary>
    private static bool references(string statement, string parameterName)
    {
        var name = parameterName.TrimStart(':', '@');
        foreach (var prefix in new[] { ':', '@' })
        {
            var token = prefix + name;
            var index = statement.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                var end = index + token.Length;
                if (end >= statement.Length || !isIdentifierChar(statement[end]))
                {
                    return true;
                }

                index = statement.IndexOf(token, end, StringComparison.Ordinal);
            }
        }

        return false;
    }

    /// <summary>
    ///     Split PostgreSQL text into its statements at the semicolons that end them, leaving alone any inside
    ///     a quoted string, a quoted identifier, a dollar-quoted body or a comment. Statements that hold
    ///     nothing but whitespace and comments are dropped.
    /// </summary>
    internal static IReadOnlyList<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        var start = 0;
        var substantive = false;
        var i = 0;

        void endStatement(int end)
        {
            if (substantive)
            {
                statements.Add(sql.Substring(start, end - start).Trim());
            }

            start = end + 1;
            substantive = false;
        }

        while (i < sql.Length)
        {
            var c = sql[i];

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var newline = sql.IndexOf('\n', i);
                i = newline < 0 ? sql.Length : newline + 1;
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                // Block comments nest in PostgreSQL
                var depth = 1;
                i += 2;
                while (i < sql.Length && depth > 0)
                {
                    if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                    {
                        depth++;
                        i += 2;
                    }
                    else if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
                    {
                        depth--;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }

                continue;
            }

            if (c == ';')
            {
                endStatement(i);
                i++;
                continue;
            }

            if (!char.IsWhiteSpace(c))
            {
                substantive = true;
            }

            if (c == '\'')
            {
                // E'...' strings take backslash escapes as well as doubled quotes
                var escapes = i > 0 && (sql[i - 1] == 'E' || sql[i - 1] == 'e') &&
                              (i < 2 || !isIdentifierChar(sql[i - 2]));
                i = skipQuoted(sql, i, '\'', escapes);
                continue;
            }

            if (c == '"')
            {
                i = skipQuoted(sql, i, '"', false);
                continue;
            }

            if (c == '$' && (i == 0 || !isIdentifierChar(sql[i - 1])) && tryReadDollarTag(sql, i, out var tag))
            {
                var close = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                i = close < 0 ? sql.Length : close + tag.Length;
                continue;
            }

            i++;
        }

        endStatement(sql.Length);
        return statements;
    }

    private static int skipQuoted(string sql, int open, char quote, bool backslashEscapes)
    {
        var i = open + 1;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (backslashEscapes && c == '\\')
            {
                i += 2;
                continue;
            }

            if (c == quote)
            {
                if (i + 1 < sql.Length && sql[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return sql.Length;
    }

    /// <summary>
    ///     A dollar quote's opening tag at <paramref name="index" />: <c>$$</c> or <c>$name$</c>, where the name
    ///     cannot start with a digit (that would be a positional parameter).
    /// </summary>
    private static bool tryReadDollarTag(string sql, int index, out string tag)
    {
        tag = string.Empty;
        var i = index + 1;
        if (i < sql.Length && char.IsDigit(sql[i]))
        {
            return false;
        }

        var name = new StringBuilder();
        while (i < sql.Length && sql[i] != '$')
        {
            if (!isIdentifierChar(sql[i]))
            {
                return false;
            }

            name.Append(sql[i]);
            i++;
        }

        if (i >= sql.Length)
        {
            return false;
        }

        tag = "$" + name + "$";
        return true;
    }

    private static bool isIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}
