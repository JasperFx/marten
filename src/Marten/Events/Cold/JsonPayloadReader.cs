#nullable enable
using System;
using System.Collections;
using System.Data.Common;
using System.IO;
using System.Text;

namespace Marten.Events.Cold;

/// <summary>
///     A one-row, one-column reader over a JSON string. Upcasters and <see cref="ISerializer" /> read event
///     payloads from a <see cref="DbDataReader" />, so this is what lets a payload that never came from
///     <c>mt_events</c> go through exactly the same code.
/// </summary>
internal sealed class JsonPayloadReader: DbDataReader
{
    private readonly string _json;
    private bool _onRow;
    private bool _consumed;

    public JsonPayloadReader(string json)
    {
        _json = json;
    }

    public override bool Read()
    {
        if (_consumed) return _onRow = false;
        _consumed = true;
        return _onRow = true;
    }

    public override T GetFieldValue<T>(int ordinal)
    {
        assertColumn(ordinal);

        if (typeof(T) == typeof(Stream)) return (T)(object)GetStream(ordinal);
        if (typeof(T) == typeof(TextReader)) return (T)(object)GetTextReader(ordinal);
        if (typeof(T) == typeof(byte[])) return (T)(object)Encoding.UTF8.GetBytes(_json);

        return (T)(object)_json;
    }

    public override Stream GetStream(int ordinal)
    {
        assertColumn(ordinal);
        return new MemoryStream(Encoding.UTF8.GetBytes(_json), false);
    }

    public override TextReader GetTextReader(int ordinal)
    {
        assertColumn(ordinal);
        return new StringReader(_json);
    }

    public override string GetString(int ordinal)
    {
        assertColumn(ordinal);
        return _json;
    }

    public override object GetValue(int ordinal) => GetString(ordinal);

    public override int GetValues(object[] values)
    {
        values[0] = _json;
        return 1;
    }

    public override bool IsDBNull(int ordinal)
    {
        assertColumn(ordinal);
        return false;
    }

    private void assertColumn(int ordinal)
    {
        if (!_onRow) throw new InvalidOperationException("No current row");
        if (ordinal != 0) throw new IndexOutOfRangeException($"A JSON payload has one column, not {ordinal + 1}");
    }

    public override int FieldCount => 1;
    public override bool HasRows => true;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override int Depth => 0;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));
    public override string GetName(int ordinal) => "data";
    public override int GetOrdinal(string name) => 0;
    public override string GetDataTypeName(int ordinal) => "jsonb";
    public override Type GetFieldType(int ordinal) => typeof(string);
    public override bool NextResult() => false;
    public override IEnumerator GetEnumerator() => throw new NotSupportedException();

    public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
    public override byte GetByte(int ordinal) => throw new NotSupportedException();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override char GetChar(int ordinal) => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
    public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
    public override double GetDouble(int ordinal) => throw new NotSupportedException();
    public override float GetFloat(int ordinal) => throw new NotSupportedException();
    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
    public override short GetInt16(int ordinal) => throw new NotSupportedException();
    public override int GetInt32(int ordinal) => throw new NotSupportedException();
    public override long GetInt64(int ordinal) => throw new NotSupportedException();
}
