using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Patching;

internal class PatchFragment: IOperationFragment
{
    private const string VALUE_LOOKUP = "___VALUE___";
    private readonly IDictionary<string, object> _patch;
    private readonly ISerializer _serializer;
    private readonly IDocumentStorage _storage;
    private readonly DbObjectName _function;

    public PatchFragment(IDictionary<string, object> patch, ISerializer serializer, DbObjectName function,
        IDocumentStorage storage, bool possiblyPolymorphic)
    {
        PossiblyPolymorphic = possiblyPolymorphic;
        _patch = patch;
        _serializer = serializer;
        _function = function;
        _storage = storage;
    }

    public bool PossiblyPolymorphic { get; }

    public void Apply(ICommandBuilder builder)
    {
        var json = _serializer.ToCleanJson(_patch);
        if (_patch.TryGetValue("value", out var document))
        {
            var value = PossiblyPolymorphic ? _serializer.ToJsonWithTypes(document) : _serializer.ToJson(document);
            var copy = new Dictionary<string, object>();
            foreach (var item in _patch) copy.Add(item.Key, item.Value);

            copy["value"] = VALUE_LOOKUP;

            var patchJson = _serializer.ToJson(copy);
            var replacedValue = patchJson.Replace($"\"{VALUE_LOOKUP}\"", value);

            json = replacedValue;
        }

        builder.Append("update ");
        builder.Append(_storage.TableName.QualifiedName);
        builder.Append(" as d set data = ");
        builder.Append(_function.QualifiedName);
        builder.Append("(data, ");
        builder.AppendParameter(json, NpgsqlDbType.Jsonb);
        builder.Append("), ");
        builder.Append(SchemaConstants.LastModifiedColumn);
        builder.Append(" = (now() at time zone 'utc'), ");
        builder.Append(SchemaConstants.VersionColumn);
        builder.Append(" = ");
        builder.AppendParameter(CombGuidIdGeneration.NewGuid());
    }

    public OperationRole Role()
    {
        return OperationRole.Patch;
    }
}

internal class PatchOperation: StatementOperation, NoDataReturnedCall
{
    private readonly ISqlFragment _fragment;
    private readonly IDocumentStorage _storage;

    public PatchOperation(DbObjectName function, IDocumentStorage storage,
        IDictionary<string, object> patch, ISerializer serializer, bool possiblyPolymorphic): base(storage,
        new PatchFragment(patch, serializer, function, storage, possiblyPolymorphic))
    {
        _storage = storage;
    }

    public OperationRole Role()
    {
        return OperationRole.Patch;
    }

    protected override void configure(ICommandBuilder builder)
    {
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

        builder.StartNewCommand();
        builder.Append("update ");
        builder.Append(_storage.TableName.QualifiedName);
        builder.Append(" as d set ");

        builder.Append(fields[0].UpdateSqlFragment());
        for (var i = 1; i < fields.Count; i++)
        {
            builder.Append(", ");
            builder.Append(fields[i].UpdateSqlFragment());
        }

        writeWhereClause(builder);
    }
}
