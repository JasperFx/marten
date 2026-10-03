#nullable enable
using System;
using System.Linq;
using System.Text;

namespace Marten.Linq.Parsing.Methods.FullText;

internal class PrefixSearch: FullTextSearchMethodCallParser
{
    public PrefixSearch(): base(nameof(LinqExtensions.PrefixSearch), FullTextSearchFunction.to_tsquery)
    {
    }

    /// <remarks>
    /// #5568: unlike <see cref="LinqExtensions.Search{T}(T,string)" />, which documents that its
    /// term may carry lexeme patterns, <see cref="LinqExtensions.PrefixSearch{T}(T,string)" /> is
    /// documented for raw input -- "each word is treated as a prefix". So a search-box string with
    /// a tsquery operator in it ("sqlite &amp; | ! (") must not reach <c>to_tsquery</c> as syntax.
    /// It used to, and came back as <c>42601: syntax error in tsquery</c>.
    /// <para>
    /// Each word becomes a single-quoted tsquery lexeme rather than being filtered down to its
    /// letters and digits. Quoting is byte-for-byte equivalent to the old output for any ordinary
    /// word -- including the compound forms the text search parser expands itself, so
    /// <c>foo-bar</c> still yields <c>'foo-bar':* &lt;-&gt; 'foo':* &lt;-&gt; 'bar':*</c> rather
    /// than being split into two independent prefixes. Operator characters inside a quoted lexeme
    /// are re-parsed as phrase separators instead of as syntax.
    /// </para>
    /// </remarks>
    protected override string TransformSearchTerm(string searchTerm)
    {
        // Split on all whitespace, not just ' '. A tab or newline in the term used to be a
        // syntax error of its own, so there is no working behavior here to preserve, and a
        // search box has no reason to treat them differently from a space.
        var words = searchTerm.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return string.Join(" & ", words.Select(quoteAsLexeme).Where(x => x != null));
    }

    /// <summary>
    /// Wrap one word as a quoted tsquery lexeme. Inside the quotes a backslash escapes the next
    /// character, so both <c>\</c> and <c>'</c> have to be doubled -- a word ending in a single
    /// backslash would otherwise escape its own closing quote and take the rest of the query
    /// with it.
    /// </summary>
    private static string? quoteAsLexeme(string word)
    {
        var builder = new StringBuilder(word.Length + 6);
        builder.Append('\'');

        foreach (var c in word)
        {
            if (c == '\\' || c == '\'') builder.Append(c);
            builder.Append(c);
        }

        // An empty quoted lexeme ('':*) is itself a tsquery syntax error. Split with
        // RemoveEmptyEntries means this can't happen today, but the cost of being sure is a
        // length check.
        if (builder.Length == 1) return null;

        builder.Append("':*");
        return builder.ToString();
    }
}
