using System;
using System.Linq;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;
public class AllComparisionFilter: ISqlFragment
{
    private readonly ISqlFragment _nestedFilter;

    public AllComparisionFilter(ISqlFragment nestedFilter)
    {
        _nestedFilter = nestedFilter ?? throw new ArgumentNullException(nameof(nestedFilter));
        if (nestedFilter is not ComparisonFilter &&
            nestedFilter is not IsNullFilter)
        {
            throw new ArgumentOutOfRangeException(nameof(nestedFilter), "Unsupported type of filter.");
        }
    }

    public void Apply(CommandBuilder builder)
    {
        switch (_nestedFilter)
        {
             case ComparisonFilter comparisonFilter:
                 ApplyUsingComparisonFilter(builder, comparisonFilter);
                 return;
             case IsNullFilter isNullFilter:
                 ApplyUsingIsNullFilter(builder, isNullFilter);
                 return;
            default:
                throw new ArgumentOutOfRangeException(nameof(_nestedFilter), "Unsupported type of filter.");
        }
    }

    private void ApplyUsingComparisonFilter(CommandBuilder builder, ComparisonFilter comparisonFilter)
    {
        switch (comparisonFilter.Left)
        {
            /*
             * Query on primitive value
             */
            case SimpleDataField leftOperand:
            {
                if (comparisonFilter.Right is CommandParameter { Value: null })
                {
                    builder.Append($" true = ALL (select unnest({leftOperand.RawLocator}) is null)");
                }
                else
                {
                    comparisonFilter.Right.Apply(builder);
                    builder.Append(" ");
                    builder.Append(comparisonFilter.Op);
                    builder.Append($" ALL ({leftOperand.RawLocator})");
                }
                break;
            }
            /*
            * Query on nested object
            */
            case FieldBase leftOperand:
            {
                var rawLocatorSegments = leftOperand.RawLocator.Split(new []{ "->>" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray();
                var parentLocator = rawLocatorSegments[0].Replace("d.", "");
                if (comparisonFilter.Right is CommandParameter { Value: null })
                {
                    builder.Append($" true = ALL (select unnest(array(select unnest({parentLocator}) ->> {rawLocatorSegments[1]})) is null)");
                }
                else
                {
                    comparisonFilter.Right.Apply(builder);
                    builder.Append(" ");
                    builder.Append(comparisonFilter.Op);
                    builder.Append($" ALL (array(select unnest({parentLocator}) ->> {rawLocatorSegments[1]}))");
                }
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(comparisonFilter.Left), "Unsupported type of operand.");
        }
    }

    private void ApplyUsingIsNullFilter(CommandBuilder builder, IsNullFilter comparisonFilter)
    {
        if (comparisonFilter.Field is not FieldBase field)
        {
            throw new ArgumentOutOfRangeException(nameof(comparisonFilter.Field), "Unsupported type of field.");
        }
        /*
         * Query on nested object
         */
        var rawLocatorSegments = field.RawLocator.Split(new []{ "->>" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToArray();
        var parentLocator = rawLocatorSegments[0].Replace("d.", "");
        builder.Append($" true = ALL (select unnest(array(select unnest({parentLocator}) ->> {rawLocatorSegments[1]})) is null)");
    }

    public bool Contains(string sqlText) => _nestedFilter.Contains(sqlText);
}
