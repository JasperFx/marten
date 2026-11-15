using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Storage;
using Marten.Storage.Metadata;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal.Storage;


internal interface IHaveMetadataColumns
{
    MetadataColumn[] MetadataColumns();
}


internal class DuplicatedFieldSelectClause: ISelectClause, IModifyableFromObject
{
    private string[] _selectFields;

    internal void EnsureColumn(string columnLocator)
    {
        if (!_selectFields.Contains(columnLocator))
        {
            _selectFields = [.._selectFields, columnLocator];
        }
    }
    private readonly IDocumentStorage _parent;

    public DuplicatedFieldSelectClause(string fromObject, string selector, string[] selectFields, Type selectedType,
        IDocumentStorage parent)
    {
        FromObject = fromObject;
        _selectFields = selectFields;
        _parent = parent;
        SelectedType = selectedType;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("select ");
        builder.Append(_selectFields.Join(", "));
        builder.Append(" from ");
        builder.Append(FromObject);
        builder.Append(" as d");
    }

    public string FromObject
    {
        get;
        set;
    }


    public Type SelectedType { get; }
    public string[] SelectFields()
    {
        return _selectFields;
    }

    public ISelector BuildSelector(IStorageSession session)
    {
        return _parent.BuildSelector(session);
    }

    public IQueryHandler<T> BuildHandler<T>(IStorageSession session, ISqlFragment topStatement, ISqlFragment currentStatement) where T : notnull
    {
        return ((ISelectClause)_parent).BuildHandler<T>(session, topStatement, currentStatement);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers supply source-generator-backed serializer impls per the AOT publishing guide.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return typeof(StatsSelectClause<>).CloseAndBuildAs<ISelectClause>(this, statistics, SelectedType);
    }
}
