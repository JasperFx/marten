using System;
using System.Globalization;
using System.Linq.Expressions;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

internal class StringCompareComparable: IComparableMember
{
    private readonly SimpleExpression _left;
    private readonly SimpleExpression _right;
    private readonly StringComparison? _stringComparison;
    private readonly bool _ignoreCase;
    private readonly CultureInfo _culture;
    private readonly CompareOptions _compareOptions;

    public StringCompareComparable(
        SimpleExpression left,
        SimpleExpression right,
        StringComparison? stringComparison = null,
        bool ignoreCase = false,
        CultureInfo culture = null,
        CompareOptions compareOptions = CompareOptions.None)
    {
        _left = left;
        _right = right;
        _stringComparison = stringComparison;
        _ignoreCase = ignoreCase;
        _culture = culture;
        _compareOptions = compareOptions;
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        var leftFragment = _left.FindValueFragment();
        var rightFragment = _right.FindValueFragment();

        // Only apply COLLATE for non-invariant cultures
        if (_culture != null && !string.IsNullOrEmpty(_culture.Name) && _culture != CultureInfo.InvariantCulture)
        {
            leftFragment = new CollatedFragment(leftFragment, _culture);
            rightFragment = new CollatedFragment(rightFragment, _culture);
        }

        // Handle case insensitivity
        var useCaseInsensitive = _ignoreCase || _compareOptions == CompareOptions.IgnoreCase ||
            (_stringComparison.HasValue &&
             (_stringComparison.Value == StringComparison.OrdinalIgnoreCase ||
              _stringComparison.Value == StringComparison.CurrentCultureIgnoreCase ||
              _stringComparison.Value == StringComparison.InvariantCultureIgnoreCase));

        if (useCaseInsensitive)
        {
            leftFragment = new LowerCaseFragment(leftFragment);
            rightFragment = new LowerCaseFragment(rightFragment);
        }

        return new ComparisonFilter(leftFragment, rightFragment, op);
    }
}
