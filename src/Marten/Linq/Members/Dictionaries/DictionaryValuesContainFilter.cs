#nullable enable
using System.Collections.Generic;
using Marten.Exceptions;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Core.Serialization;
using Weasel.Postgresql;
using CommandBuilder = Weasel.Postgresql.CommandBuilder;
using ConstantExpression = System.Linq.Expressions.ConstantExpression;
using ISqlFragment = Weasel.Postgresql.SqlGeneration.ISqlFragment;


namespace Marten.Linq.Members.Dictionaries;

internal class DictionaryValuesContainFilter: ISqlFragment, ICollectionAware, ICollectionAwareFilter
{
    private readonly IDictionaryMember _member;
    private readonly string _text;

    public DictionaryValuesContainFilter(IDictionaryMember member, ISerializer serializer, ConstantExpression constant)
    {
        _member = member;

        _text = constant.Value as string ?? serializer.ToCleanJson(constant.Value);
    }

    public DictionaryValuesContainFilter(IDictionaryMember member, ISerializer serializer, object value)
    {
        _member = member;

        _text = value is string s ? s : serializer.ToCleanJson(value);
    }

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        builder.Append("jsonb_path_query_array(");
        builder.Append(_member.TypedLocator);
        builder.Append(", '$.*') ? ");


        builder.AppendParameter(_text);
        builder.Append(" = true");
    }

    public bool CanReduceInChildCollection()
    {
        return true;
    }

    public ICollectionAwareFilter BuildFragment(ICollectionMember member, ISerializer serializer)
    {
        return this;
    }

    public bool SupportsContainment()
    {
        return false;
    }

    public void PlaceIntoContainmentFilter(ContainmentWhereFilter filter)
    {
        throw new System.NotSupportedException();
    }

    public bool CanBeJsonPathFilter()
    {
        throw new System.NotSupportedException();
    }

    public void BuildJsonPathFilter(IPostgresqlCommandBuilder builder, Dictionary<string, object> parameters)
    {
        throw new System.NotSupportedException();
    }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        throw new BadLinqExpressionException(
            "The Dictionary.Values.Contains() queries are not (yet) supported in compiled queries");
    }

    public ICollectionMember CollectionMember => _member;
    public ISqlFragment MoveUnder(ICollectionMember ancestorCollection)
    {
        return this;
    }
}
