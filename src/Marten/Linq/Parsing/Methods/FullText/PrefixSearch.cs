#nullable enable
using System;
using System.Linq;

namespace Marten.Linq.Parsing.Methods.FullText;

internal class PrefixSearch: FullTextSearchMethodCallParser
{
    public PrefixSearch(): base(nameof(LinqExtensions.PrefixSearch), FullTextSearchFunction.to_tsquery)
    {
    }

    protected override string TransformSearchTerm(string searchTerm)
    {
        var words = searchTerm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" & ", words.Select(w => w + ":*"));
    }
}
