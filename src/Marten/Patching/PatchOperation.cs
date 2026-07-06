using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Marten.Services;
using Marten.Util;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Patching;

internal class PatchFragment: IOperationFragment
{
    // 9.0 (#4384): the sentinel byte pattern that ISerializer emits for the string
    // VALUE_LOOKUP when serializing the wrapper dictionary. We splice the pre-serialized
    // value bytes in at this exact boundary, preserving byte-equivalence with the
    // legacy string.Replace path. The bytes are: " _ _ _ V A L U E _ _ _ " (13 bytes,
    // ASCII / UTF-8 identical).
    private static ReadOnlySpan<byte> SentinelBytes => "\"___VALUE___\""u8;

    private const string VALUE_LOOKUP = "___VALUE___";
    private readonly ISerializer _serializer;
    private readonly IDocumentStorage _storage;
    private readonly DbObjectName _function;
    private readonly DocumentSessionBase _session;
    private readonly List<PatchData> _patchSet;

    public PatchFragment(DocumentSessionBase session, List<PatchData> patchSet, ISerializer serializer,
        DbObjectName function,
        IDocumentStorage storage)
    {
        _session = session;
        _patchSet = patchSet;
        _serializer = serializer;
        _function = function;
        _storage = storage;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("update ");
        builder.Append(_storage.TableName.QualifiedName);
        builder.Append(" as d set data = ");
        builder.Append(_function.QualifiedName);
        builder.Append("(data, ");

        // 9.0 (#4384): build the JSON array body directly into a pooled UTF-8 buffer and
        // bind as byte[]. Replaces a 4-5-string-per-patch + dict-copy + string.Replace
        // sentinel-substitution pipeline. Byte-equivalent with the prior string-based
        // emission — guarded by PatchFragmentByteEquivalenceTests.
        using (var body = new PooledByteBufferWriter(initialCapacity: 1024))
        {
            writePatchArray(body);
            builder.AppendParameter(body.ToSizedArray(), NpgsqlDbType.Jsonb);
        }

        builder.Append(")");

        if (_storage is IHaveMetadataColumns metadata)
        {
            foreach (var column in metadata.MetadataColumns().Where(x => x.Enabled && x.ShouldUpdatePartials))
            {
                builder.Append(", ");
                column.WriteMetadataInUpdateStatement(builder, _session);
            }
        }
    }

    /// <summary>
    /// Emit the patch-set JSON array directly into <paramref name="body"/> as UTF-8 bytes,
    /// reproducing what the prior implementation would have produced via the
    /// <c>"[" + string.Join(",", patchSetStr) + "]"</c> string concatenation but without
    /// the per-patch intermediate string allocations.
    /// </summary>
    /// <remarks>
    /// Internal so the byte-equivalence test (which lives in CoreTests) can drive this
    /// method directly without re-walking the SQL fragment emission.
    /// </remarks>
    internal void writePatchArray(IBufferWriter<byte> body)
    {
        body.GetSpan(1)[0] = (byte)'[';
        body.Advance(1);
        for (var i = 0; i < _patchSet.Count; i++)
        {
            if (i > 0)
            {
                body.GetSpan(1)[0] = (byte)',';
                body.Advance(1);
            }
            writePatchJson(body, _patchSet[i]);
        }
        body.GetSpan(1)[0] = (byte)']';
        body.Advance(1);
    }

    private void writePatchJson(IBufferWriter<byte> body, PatchData patch)
    {
        if (!patch.Items.TryGetValue("value", out var document))
        {
            // No "value" key — emit the items as clean JSON (no $type metadata), matching
            // the legacy `_serializer.ToCleanJson(patch.Items)` byte output.
            _serializer.WriteToCleanJson(body, patch.Items);
            return;
        }

        // The legacy flow:
        //   1. value = ToJsonWithTypes(document) | ToJson(document)   (depending on polymorphism)
        //   2. copy = clone of patch.Items with value=VALUE_LOOKUP
        //   3. patchJson = ToJson(copy)                                 // wrapper with sentinel
        //   4. final = patchJson.Replace("\"___VALUE___\"", value)
        //
        // The new flow stages (1) into one pooled buffer, stages (3) into a second pooled
        // buffer, then writes pre-sentinel | value-bytes | post-sentinel into the body
        // buffer. Byte boundaries match because the sentinel splice happens at the same
        // position the string.Replace would have used.
        using var valueBuffer = new PooledByteBufferWriter(initialCapacity: 256);
        if (patch.PossiblyPolymorphic)
        {
            _serializer.WriteToJsonWithTypes(valueBuffer, document);
        }
        else
        {
            _serializer.WriteTo(valueBuffer, document);
        }

        using var wrapperBuffer = new PooledByteBufferWriter(initialCapacity: 256);
        // Reuse patch.Items shape with the sentinel substituted for value. A copy is
        // required because patch.Items is owned by the caller and mutating it would leak
        // the sentinel into reuse scenarios (the PatchExpression is held across Apply
        // calls in some configurations).
        var copy = new Dictionary<string, object>(patch.Items.Count);
        foreach (var item in patch.Items) copy[item.Key] = item.Value;
        copy["value"] = VALUE_LOOKUP;
        _serializer.WriteTo(wrapperBuffer, copy);

        var wrapperSpan = wrapperBuffer.WrittenSpan;
        var sentinelIdx = wrapperSpan.IndexOf(SentinelBytes);
        if (sentinelIdx < 0)
        {
            // Sentinel not present (theoretical safety net — the serializer should always
            // emit the literal "___VALUE___" string for the substituted value). Emit the
            // wrapper unmodified rather than corrupting the output silently.
            body.Write(wrapperSpan);
            return;
        }

        body.Write(wrapperSpan.Slice(0, sentinelIdx));
        body.Write(valueBuffer.WrittenSpan);
        body.Write(wrapperSpan.Slice(sentinelIdx + SentinelBytes.Length));
    }

    public OperationRole Role()
    {
        return OperationRole.Patch;
    }
}

internal class PatchOperation: StatementOperation, NoDataReturnedCall
{
    private readonly IDocumentStorage _storage;
    private readonly List<PatchData> _patchSet;
    private readonly ISerializer _serializer;

    public PatchOperation(DocumentSessionBase session, DbObjectName function, IDocumentStorage storage,
        List<PatchData> patchSet, ISerializer serializer):
        base(storage, new PatchFragment(session, patchSet, serializer, function, storage))
    {
        _storage = storage;
        _patchSet = patchSet;
        _serializer = serializer;
    }

    public OperationRole Role()
    {
        return OperationRole.Patch;
    }

    protected override void configure(ICommandBuilder builder)
    {
        if (_patchSet.Count == 0) return;
        base.configure(builder);
        applyUpdates(builder);
    }

    private void applyUpdates(ICommandBuilder builder)
    {
        var fields = _storage.DuplicatedFields;
        if (!fields.Any())
        {
            return;
        }

        // Get the paths being modified by this patch operation
        var modifiedPaths = _patchSet
            .Select(patch => patch.Items["path"].ToString())
            .ToHashSet(System.StringComparer.Ordinal);

        // Only update duplicated fields where their mapping path is affected by the patch path
        var affectedFields = fields.Where(f => IsFieldAffectedByPatchPath(f, modifiedPaths)).ToList();

        if (affectedFields.Count == 0)
        {
            return;
        }

        builder.StartNewCommand();
        builder.Append("update ");
        builder.Append(_storage.TableName.QualifiedName);
        builder.Append(" as d set ");

        builder.Append(affectedFields[0].UpdateSqlFragment());
        for (var i = 1; i < affectedFields.Count; i++)
        {
            builder.Append(", ");
            builder.Append(affectedFields[i].UpdateSqlFragment());
        }

        writeWhereClause(builder);
    }

    private bool IsFieldAffectedByPatchPath(IDuplicatedField field, HashSet<string> modifiedPaths)
    {
        // get the dot seperated path derived from field Members info
        var path = string.Join('.', field.Members.Select(x => x.Name.FormatCase(_serializer.Casing)));
        return modifiedPaths.Any(p => p.StartsWith(path, StringComparison.Ordinal));
    }
}
