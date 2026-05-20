using System;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.Parsing;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Util;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Storage.Metadata;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
public abstract class MetadataColumn: TableColumn
{
    protected MetadataColumn(string name, string type, Type dotNetType): base(name, type)
    {
        DotNetType = dotNetType;
    }

    public Type DotNetType { get; }

    public abstract MemberInfo Member { get; set; }

    /// <summary>
    ///     Is this metadata column enabled?
    /// </summary>
    public bool Enabled { get; set; } = true;

    public bool ShouldUpdatePartials { get; protected set; }

    internal abstract Task ApplyAsync(IMartenSession martenSession, DocumentMetadata metadata, int index,
        DbDataReader reader, CancellationToken token);

    internal abstract void Apply(IMartenSession martenSession, DocumentMetadata metadata, int index,
        DbDataReader reader);

    internal virtual void RegisterForLinqSearching(DocumentMapping mapping)
    {
        if (!Enabled || Member == null)
        {
            return;
        }

        mapping.DuplicateField(new[] { Member }, columnName: Name)
            .OnlyForSearching = true;
    }

    public bool EnabledWithMember()
    {
        return Enabled && Member != null;
    }

    internal virtual UpsertArgument ToArgument()
    {
        return new UpsertArgument
        {
            Arg = "arg_" + Name,
            Column = Name,
            DbType = PostgresqlProvider.Instance.ToParameterType(DotNetType),
            PostgresType = Type,
            Members = new[] { Member }
        };
    }

    public virtual void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        throw new NotSupportedException();
    }
}

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
internal abstract class MetadataColumn<T>: MetadataColumn
{
    private readonly string _memberName;
    private readonly Action<DocumentMetadata, T> _setter;
    private MemberInfo _member;

    protected MetadataColumn(string name, Expression<Func<DocumentMetadata, T>> property): base(name,
        PostgresqlProvider.Instance.GetDatabaseType(typeof(T), EnumStorage.AsInteger), typeof(T))
    {
        var member = MemberFinder.Determine(property).Last();
        _memberName = member.Name;
        _setter = LambdaBuilder.Setter<DocumentMetadata, T>(member);
    }

    public override MemberInfo Member
    {
        get => _member;
        set
        {
            if (value != null)
            {
                if (!IsAcceptableMemberType(value.GetRawMemberType()!))
                {
                    throw new ArgumentOutOfRangeException(nameof(value),
                        $"The {_memberName} member of {value.DeclaringType?.NameInCode() ?? "null"} has to be of type {typeof(T).NameInCode()}");
                }

                _member = value;
                Enabled = true;
            }
        }
    }

    /// <summary>
    /// Whether a document member of the given type can back this metadata column.
    /// Defaults to an exact match on <typeparamref name="T"/>; the revision column
    /// overrides this to accept both int (IRevisioned) and long (ILongVersioned).
    /// </summary>
    protected virtual bool IsAcceptableMemberType(Type memberType) => memberType == typeof(T);

    internal override async Task ApplyAsync(IMartenSession martenSession, DocumentMetadata metadata, int index,
        DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(index, token).ConfigureAwait(false))
        {
            return;
        }

        var value = await reader.GetFieldValueAsync<T>(index, token).ConfigureAwait(false);
        _setter(metadata, value);
    }

    internal override void Apply(IMartenSession martenSession, DocumentMetadata metadata, int index,
        DbDataReader reader)
    {
        if (reader.IsDBNull(index))
        {
            return;
        }

        var value = reader.GetFieldValue<T>(index);
        _setter(metadata, value);
    }
}
