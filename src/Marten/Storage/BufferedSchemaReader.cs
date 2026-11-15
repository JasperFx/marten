#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Marten.Storage;

/// <summary>
///     #5969: every result set of a schema introspection command, read synchronously and held in memory, so
///     that Weasel's delta readers (<c>ISchemaObject.CreateDeltaAsync</c>) can be run over it without any I/O.
/// </summary>
/// <remarks>
///     <para>
///     The asynchronous members are deliberately left to <see cref="DbDataReader" />, whose implementations
///     call the synchronous ones and hand back an already completed task. With nothing left to wait for, a
///     delta reader awaiting <c>ReadAsync</c>, <c>NextResultAsync</c> or <c>GetFieldValueAsync</c> completes
///     on the calling thread and never needs a thread-pool thread.
///     </para>
///     <para>
///     Values are buffered as the driver returns them from <c>GetValues</c>, which is what its own
///     <c>GetFieldValue</c> hands back for the column's default CLR type. Only the handful of tables and
///     functions behind Marten's HiLo sequences are read through this, so it does not try to reproduce every
///     conversion Npgsql can make.
///     </para>
/// </remarks>
internal sealed class BufferedSchemaReader: DbDataReader
{
    private readonly List<ResultSet> _results;
    private int _resultIndex;
    private int _rowIndex = -1;
    private bool _closed;

    private BufferedSchemaReader(List<ResultSet> results)
    {
        _results = results.Count > 0
            ? results
            : [new ResultSet([], [], [], [])];
    }

    /// <summary>
    ///     Read every row of every result set from <paramref name="reader" />, synchronously.
    /// </summary>
    public static BufferedSchemaReader ReadAll(DbDataReader reader)
    {
        var results = new List<ResultSet>();
        Capture(reader, results);
        return new BufferedSchemaReader(results);
    }

    /// <summary>
    ///     One reader over the result sets of several commands, in the order they were captured.
    /// </summary>
    public static BufferedSchemaReader Over(List<ResultSet> results) => new(results);

    /// <summary>
    ///     Append every result set of <paramref name="reader" /> to <paramref name="results" />,
    ///     reading synchronously.
    /// </summary>
    public static void Capture(DbDataReader reader, List<ResultSet> results)
    {
        do
        {
            var fieldCount = reader.FieldCount;
            if (fieldCount == 0)
            {
                // A statement that returns no rows at all (no RowDescription) is not a result set
                continue;
            }

            var names = new string[fieldCount];
            var types = new Type[fieldCount];
            var dataTypeNames = new string[fieldCount];
            for (var i = 0; i < fieldCount; i++)
            {
                names[i] = reader.GetName(i);
                types[i] = reader.GetFieldType(i);
                dataTypeNames[i] = reader.GetDataTypeName(i);
            }

            var rows = new List<object[]>();
            while (reader.Read())
            {
                var values = new object[fieldCount];
                reader.GetValues(values);
                rows.Add(values);
            }

            results.Add(new ResultSet(names, types, dataTypeNames, rows));
        } while (reader.NextResult());
    }

    private ResultSet Current => _results[_resultIndex];

    private object[] CurrentRow
    {
        get
        {
            if (_rowIndex < 0 || _rowIndex >= Current.Rows.Count)
            {
                throw new InvalidOperationException("No row is available to read");
            }

            return Current.Rows[_rowIndex];
        }
    }

    public override bool Read()
    {
        if (_closed || _rowIndex >= Current.Rows.Count)
        {
            return false;
        }

        _rowIndex++;
        return _rowIndex < Current.Rows.Count;
    }

    public override bool NextResult()
    {
        if (_closed || _resultIndex >= _results.Count - 1)
        {
            // Past the last result set there is nothing more to read, as with a driver's reader
            _rowIndex = int.MaxValue;
            return false;
        }

        _resultIndex++;
        _rowIndex = -1;
        return true;
    }

    public override T GetFieldValue<T>(int ordinal)
    {
        var value = CurrentRow[ordinal];

        if (value is T typed)
        {
            return typed;
        }

        if (value is DBNull)
        {
            if (default(T) == null && (typeof(T) == typeof(object) || Nullable.GetUnderlyingType(typeof(T)) != null))
            {
                return default!;
            }

            throw new InvalidCastException(
                $"Column '{Current.Names[ordinal]}' is null and cannot be read as {typeof(T).Name}");
        }

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(target))
        {
            return (T)Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        }

        throw new InvalidCastException(
            $"Column '{Current.Names[ordinal]}' holds a {value.GetType().Name} and cannot be read as {typeof(T).Name}");
    }

    public override object GetValue(int ordinal) => CurrentRow[ordinal];

    public override int GetValues(object[] values)
    {
        var row = CurrentRow;
        var count = Math.Min(values.Length, row.Length);
        Array.Copy(row, values, count);
        return count;
    }

    public override bool IsDBNull(int ordinal) => CurrentRow[ordinal] is DBNull;

    public override int FieldCount => _closed ? 0 : Current.Names.Length;

    public override string GetName(int ordinal) => Current.Names[ordinal];

    public override int GetOrdinal(string name)
    {
        var names = Current.Names;
        for (var i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal)) return i;
        }

        for (var i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        }

        throw new IndexOutOfRangeException($"No column named '{name}'");
    }

    // The types are the driver's own answers for the buffered columns, passed back unchanged
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields |
                                        DynamicallyAccessedMemberTypes.PublicProperties)]
    [UnconditionalSuppressMessage("Trimming", "IL2063",
        Justification = "Hands back the type the driver's reader reported for the same column")]
    public override Type GetFieldType(int ordinal) => Current.Types[ordinal];

    public override string GetDataTypeName(int ordinal) => Current.DataTypeNames[ordinal];

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool HasRows => !_closed && Current.Rows.Count > 0;

    public override bool IsClosed => _closed;

    public override int RecordsAffected => -1;

    public override int Depth => 0;

    public override void Close()
    {
        _closed = true;
    }

    public override bool GetBoolean(int ordinal) => GetFieldValue<bool>(ordinal);

    public override byte GetByte(int ordinal) => GetFieldValue<byte>(ordinal);

    public override char GetChar(int ordinal) => GetFieldValue<char>(ordinal);

    public override DateTime GetDateTime(int ordinal) => GetFieldValue<DateTime>(ordinal);

    public override decimal GetDecimal(int ordinal) => GetFieldValue<decimal>(ordinal);

    public override double GetDouble(int ordinal) => GetFieldValue<double>(ordinal);

    public override float GetFloat(int ordinal) => GetFieldValue<float>(ordinal);

    public override Guid GetGuid(int ordinal) => GetFieldValue<Guid>(ordinal);

    public override short GetInt16(int ordinal) => GetFieldValue<short>(ordinal);

    public override int GetInt32(int ordinal) => GetFieldValue<int>(ordinal);

    public override long GetInt64(int ordinal) => GetFieldValue<long>(ordinal);

    public override string GetString(int ordinal) => GetFieldValue<string>(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        return copy(GetFieldValue<byte[]>(ordinal), dataOffset, buffer, bufferOffset, length);
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        return copy(GetFieldValue<string>(ordinal).ToCharArray(), dataOffset, buffer, bufferOffset, length);
    }

    public override Stream GetStream(int ordinal) => new MemoryStream(GetFieldValue<byte[]>(ordinal), false);

    public override TextReader GetTextReader(int ordinal) => new StringReader(GetFieldValue<string>(ordinal));

    public override IEnumerator GetEnumerator() => new DbEnumerator(this);

    private static long copy<T>(T[] source, long dataOffset, T[]? buffer, int bufferOffset, int length)
    {
        if (buffer == null)
        {
            return source.Length;
        }

        var count = (int)Math.Max(0, Math.Min(length, source.Length - dataOffset));
        Array.Copy(source, dataOffset, buffer, bufferOffset, count);
        return count;
    }

    internal sealed record ResultSet(string[] Names, Type[] Types, string[] DataTypeNames, List<object[]> Rows);
}
