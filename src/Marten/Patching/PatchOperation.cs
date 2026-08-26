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

internal class PatchFragment: IOperationFragment, ISqlFragment
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

        var modifiedPaths = collectModifiedPaths();

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

    /// <summary>
    /// Every JSON location this patch set writes to, in the same form <c>PatchExpression.toPath</c> emits.
    /// </summary>
    /// <remarks>
    /// #5295: "path" is not the only place a patch writes. A <c>duplicate</c> carries its destinations in
    /// "targets", and a <c>rename</c> puts the value at a sibling of "path" named by "to" — a duplicated
    /// column over either location went stale because neither was ever considered.
    /// </remarks>
    private HashSet<string> collectModifiedPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var patch in _patchSet)
        {
            if (!patch.Items.TryGetValue("path", out var raw) || raw?.ToString() is not { } path) continue;

            paths.Add(path);

            if (patch.Items.TryGetValue("targets", out var targets) && targets is IEnumerable<string> destinations)
            {
                foreach (var destination in destinations) paths.Add(destination);
            }

            if (patch.Items.TryGetValue("to", out var to) && to?.ToString() is { Length: > 0 } newName)
            {
                var lastSeparator = path.LastIndexOf('.');
                paths.Add(lastSeparator < 0 ? newName : string.Concat(path.AsSpan(0, lastSeparator + 1), newName));
            }
        }

        return paths;
    }

    private bool IsFieldAffectedByPatchPath(IDuplicatedField field, HashSet<string> modifiedPaths)
    {
        // #5290/#5295: ToJsonKey, not Name.FormatCase. PatchExpression.toPath resolves serializer member
        // aliases, so on an aliased member the two disagreed -- the patch wrote "st" and this looked for
        // "AliasedStatus", found no overlap, and left the duplicated column holding the old value while
        // the document held the new one.
        var path = string.Join('.', field.Members.Select(x => x.ToJsonKey(_serializer.Casing)));

        return modifiedPaths.Any(p => pathsOverlap(path, p));
    }

    /// <summary>
    /// True when writing <paramref name="patchPath" /> can change the value at <paramref name="fieldPath" />.
    /// </summary>
    /// <remarks>
    /// #5295. Overlap runs in BOTH directions and only on a separator boundary. Patching a parent moves
    /// everything beneath it, so <c>Set(x =&gt; x.Job, ...)</c> has to refresh a column duplicated from
    /// <c>Job.Progress</c> — the previous one-way prefix test missed exactly that and left the column
    /// stale. The boundary check is what stops <c>Status</c> and <c>StatusCode</c> from matching each
    /// other, which the old test also got wrong, though only ever by doing harmless extra work.
    /// </remarks>
    private static bool pathsOverlap(string fieldPath, string patchPath)
    {
        if (fieldPath.Length == patchPath.Length)
        {
            return string.Equals(fieldPath, patchPath, StringComparison.Ordinal);
        }

        var (shorter, longer) = fieldPath.Length < patchPath.Length
            ? (fieldPath, patchPath)
            : (patchPath, fieldPath);

        return longer.StartsWith(shorter, StringComparison.Ordinal) && longer[shorter.Length] == '.';
    }
}
