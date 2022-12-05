using System.Linq.Expressions;
using Marten.Linq.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Fields;

public class HasValueField: IComparableFragment
{
    private readonly IField _field;

    public HasValueField(IField field)
    {
        _field = field;
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression value, Expression memberExpression)
    {
        var hasValue = (bool)value.Value;
        return hasValue
            ? new IsNotNullFilter(_field)
            : new IsNullFilter(_field);
    }
}
