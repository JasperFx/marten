using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Internal.Operations;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Storage;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5213, hardening the class of bug behind #5210.
///
/// <para>
/// <c>OperationPage.ApplyCallbacksAsync</c> deliberately skips <c>NextResultAsync()</c> for any
/// operation marked <see cref="NoDataReturnedCall" /> — that is the entire point of the marker.
/// So an operation that declares the marker while its SQL actually <em>does</em> return a result
/// set leaves the batched reader one result set behind, and every <em>subsequent</em> operation in
/// that batch then reads the wrong one. #5210 was exactly this: <c>NotifyEventAppendedOperation</c>
/// carried the marker but emitted <c>select pg_notify(...)</c>, which returns a one-row `void`
/// column, so an inline projection upsert later in the same batch read the pg_notify row instead
/// of its own RETURNING row — surfacing as spurious ConcurrencyExceptions and wrong versions, far
/// from the cause.
/// </para>
///
/// <para>
/// This turns "the marker tells the truth" into a red test. It reflects over every
/// <see cref="NoDataReturnedCall" /> in the Marten assembly, captures the SQL each one emits, and
/// asserts the statement cannot produce a result set. The day someone writes
/// <c>select some_function(...)</c> instead of the <c>DO $$ ... PERFORM ... $$</c> form, this fails
/// with the offending type named, instead of a version mismatch showing up three operations later.
/// </para>
///
/// <para>
/// #5222: an operation is only reported as passing when the audit actually saw its SQL. Anything it
/// could not reconstruct — including an operation whose entire statement is a string field, so an
/// uninitialized instance yields nothing but <see cref="StandIn" /> — is a visible skip. See the note
/// on <see cref="StandIn" /> for the false pass that motivated this.
/// </para>
/// </summary>
public class no_data_returned_call_sql_audit
{
    /// <summary>
    /// A statement returns a result set if it is a SELECT/VALUES/TABLE/SHOW at the top level, or
    /// if it is a DML statement carrying a RETURNING clause. Everything else — INSERT/UPDATE/DELETE
    /// without RETURNING, and DO blocks (which are utility statements and cannot return rows even
    /// when they PERFORM a function internally) — does not.
    /// </summary>
    private static readonly Regex ReturnsRows =
        new(@"^\s*(select|values|table|show)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HasReturning =
        new(@"\breturning\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// #5222. The value every string field of an uninitialized stand-in is set to, deliberately
    /// chosen so it cannot be mistaken for SQL.
    ///
    /// <para>
    /// It used to be <c>"placeholder"</c>, which reads as a perfectly ordinary bareword to
    /// <see cref="ReturnsRows" />. That mattered because an operation's entire statement can *be* a
    /// string field: <c>ExecuteSqlStorageOperation._commandText</c> holds the SQL a caller handed to
    /// <c>QueueSqlCommand</c>. Its <c>ConfigureCommand</c> appended the stand-in and then threw on the
    /// parameter values, <see cref="TryCaptureSql" /> read back non-empty text, and the audit reported
    /// the operation as PASSING — having asserted only that the literal string "placeholder" does not
    /// return a result set. Unlike the five genuine skips, that gap was invisible: it read as 1 of the
    /// 15 audited.
    /// </para>
    /// </summary>
    private const string StandIn = "__marten_audit_stand_in__";

    public static IEnumerable<object[]> NoDataReturnedCallTypes()
    {
        return typeof(IDocumentStore).Assembly
            .GetTypes()
            .Where(x => x is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .Where(x => x.CanBeCastTo<NoDataReturnedCall>())
            .Where(x => x.CanBeCastTo<IStorageOperation>())
            .OrderBy(x => x.FullName)
            .Select(x => new object[] { x });
    }

    [Theory]
    [MemberData(nameof(NoDataReturnedCallTypes))]
    public void sql_emitted_by_a_no_data_returned_call_must_not_return_a_result_set(Type operationType)
    {
        if (!TryCaptureSql(operationType, out var sql, out var skipReason))
        {
            // Deliberately not a silent pass. Either the operation's ConfigureCommand needs state an
            // uninitialized instance does not have, or (#5222) its statement never got fabricated at
            // all. Skipping keeps the coverage gap visible in the run output rather than pretending
            // it was audited.
            Assert.Skip(skipReason);
            return;
        }

        var statements = SplitStatements(sql);
        statements.ShouldNotBeEmpty($"{operationType.FullNameInCode()} emitted no SQL at all");

        foreach (var statement in statements)
        {
            ReturnsRows.IsMatch(statement).ShouldBeFalse(
                $"{operationType.FullNameInCode()} is marked NoDataReturnedCall, but emits a statement that returns a result set:{Environment.NewLine}  {statement}{Environment.NewLine}" +
                "OperationPage.ApplyCallbacksAsync will not advance the reader past it, so the next operation in the batch will read this result set instead of its own. " +
                "Use the DO $$ BEGIN PERFORM ...; END $$ form (see NotifyEventAppendedOperation and #5210), or drop the NoDataReturnedCall marker.");

            HasReturning.IsMatch(statement).ShouldBeFalse(
                $"{operationType.FullNameInCode()} is marked NoDataReturnedCall, but emits a RETURNING clause, which produces a result set:{Environment.NewLine}  {statement}");
        }
    }

    [Fact]
    public void the_audit_actually_found_operations_to_audit()
    {
        // Guards the reflection query itself. If NoDataReturnedCall moves assembly again (it already
        // moved to Weasel.Storage in #4821) or the marker is renamed, the Theory above would quietly
        // become zero test cases and the invariant would go unenforced without anything going red.
        NoDataReturnedCallTypes().Count().ShouldBeGreaterThan(10);
    }

    [Fact]
    public void the_regression_case_from_5210_is_pinned_directly()
    {
        // The specific operation that caused #5210, asserted by name rather than only through the
        // reflective sweep, so the pin survives any future change to how the sweep discovers types.
        var sql = Marten.Events.Operations.NotifyEventAppendedOperation.Sql;

        ReturnsRows.IsMatch(sql).ShouldBeFalse();
        sql.ShouldContain("PERFORM pg_notify");
        sql.ShouldStartWith("DO $$");
    }

    [Fact]
    public void an_operation_whose_sql_is_caller_supplied_is_skipped_not_passed()
    {
        // #5222, pinned by name. ExecuteSqlStorageOperation is the operation that most needs
        // auditing and the one a static audit can say the least about: its statement is whatever the
        // caller passed to QueueSqlCommand. It must report as a visible skip. Before the stand-in
        // became unmistakable it reported as a PASS, having audited the string "placeholder".
        TryCaptureSql(typeof(ExecuteSqlStorageOperation), out _, out var skipReason).ShouldBeFalse();

        skipReason.ShouldContain("QueueSqlCommand");
    }

    [Fact]
    public void the_stand_in_stays_distinctive_enough_to_strip_safely()
    {
        // The "is this only stand-in?" check works by removing every occurrence of StandIn from the
        // captured text and asking whether anything is left. That is only sound while the stand-in is
        // a token no real statement would ever contain: shorten it to something like "a" or "id" and
        // the removal would start eating real SQL, turning audited operations into silent skips —
        // the same invisible-gap failure #5222 was about, pointed the other way.
        StandIn.Length.ShouldBeGreaterThan(12);
        StandIn.ShouldStartWith("__");
        StandIn.ShouldEndWith("__");
        StandIn.ShouldNotContain(" ");

        // And it must not read as a statement in its own right, or a whole-statement string field
        // would fail the audit for the wrong reason instead of skipping.
        ReturnsRows.IsMatch(StandIn).ShouldBeFalse();
        HasReturning.IsMatch(StandIn).ShouldBeFalse();
    }

    private static bool TryCaptureSql(Type operationType, out string sql, out string skipReason)
    {
        sql = string.Empty;
        skipReason =
            $"{operationType.FullNameInCode()}.ConfigureCommand could not be driven from an uninitialized instance, so its SQL was not audited.";

        // No constructor call. These operations take documents, streams, tags and sessions that are
        // impractical to fabricate here, so the instance is created uninitialized and its reference
        // fields are back-filled with equally uninitialized stand-ins. Table names and ids come out
        // as nulls or placeholders, which is fine: this audit only reads the *shape* of the
        // statement, never its values.
        var operation = (IStorageOperation)CreateStandIn(operationType, 0);
        var builder = new CommandBuilder();

        try
        {
            operation.ConfigureCommand(builder, null);
        }
        catch (Exception)
        {
            // Expected for many operations: the SQL literal is appended first and the throw comes
            // later, while binding parameter *values* off state we could not fabricate. Whatever
            // was appended before the throw is still the statement text we need to audit, so fall
            // through and read it rather than giving up.
        }

        try
        {
            sql = builder.Compile().CommandText;
        }
        catch (Exception)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

        // #5222: whatever came back is made entirely of stand-in values, so no SQL of this
        // operation's own ever got fabricated and there is nothing here to audit. Asserting against
        // it would only assert that the stand-in itself is not a SELECT.
        if (string.IsNullOrWhiteSpace(sql.Replace(StandIn, string.Empty)))
        {
            skipReason =
                $"{operationType.FullNameInCode()} emitted no SQL of its own — its whole statement came from a string field, so an uninitialized instance yields only the stand-in and there is nothing static to audit. " +
                "For ExecuteSqlStorageOperation this is permanent: the SQL comes from the caller through QueueSqlCommand, so no audit of the type can say anything about it.";
            return false;
        }

        return true;
    }

    private static object CreateStandIn(Type type, int depth)
    {
        var instance = RuntimeHelpers.GetUninitializedObject(type);
        if (depth >= 3)
        {
            return instance;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var fieldType = field.FieldType;

            if (fieldType == typeof(string))
            {
                field.SetValue(instance, StandIn);
                continue;
            }

            // Interfaces and abstracts cannot be fabricated this way, and value types already have
            // a usable default. Self-references would recurse forever.
            if (fieldType.IsValueType || fieldType.IsInterface || fieldType.IsAbstract ||
                fieldType == type || fieldType.IsArray || fieldType.IsGenericTypeDefinition)
            {
                continue;
            }

            try
            {
                field.SetValue(instance, CreateStandIn(fieldType, depth + 1));
            }
            catch (Exception)
            {
                // Leave it null; ConfigureCommand may well not touch it before appending its SQL.
            }
        }

        return instance;
    }

    private static string[] SplitStatements(string sql)
    {
        // A DO block is a single statement and its body legitimately contains semicolons, so it must
        // not be split on them — the PERFORM inside is not a top-level statement.
        if (sql.TrimStart().StartsWith("DO ", StringComparison.OrdinalIgnoreCase))
        {
            return [sql];
        }

        return sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
