using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LinqTests.ChildCollections;
using Marten;
using Marten.Testing.Documents;

namespace LinqTests.Acceptance;

// Compile-only samples for docs/documents/querying/linq/strings.md. Nothing here runs as a test;
// the behavior each sample shows is covered by the tests named next to it.
public class strings_documentation_samples
{
    // Covered by Acceptance/string_filtering.cs and Bugs/is_null_or_white_space_matches_all_whitespace.cs
    #region sample_string_is_null_or_empty
    public async Task is_null_or_empty(IQuerySession session)
    {
        var noNickname = await session.Query<User>().Where(x => string.IsNullOrEmpty(x.Nickname)).ToListAsync();

        // Matches null and any run of whitespace, including tabs and line breaks
        var blank = await session.Query<User>().Where(x => string.IsNullOrWhiteSpace(x.Nickname)).ToListAsync();

        var hasNickname = await session.Query<User>().Where(x => !string.IsNullOrWhiteSpace(x.Nickname))
            .ToListAsync();
    }
    #endregion

    // Covered by Acceptance/where_clauses.cs and Bugs/Bug_5941_string_members_after_select_many.cs
    #region sample_string_case_trim_and_length
    public async Task case_trim_and_length(IQuerySession session)
    {
        var red = await session.Query<Target>().Where(x => x.String.ToLower() == "red").ToListAsync();
        var green = await session.Query<Target>().Where(x => x.String.ToUpperInvariant() == "GREEN").ToListAsync();
        var padded = await session.Query<Target>().Where(x => x.PaddedString.Trim() == "red").ToListAsync();
        var shortOnes = await session.Query<Target>().Where(x => x.String.Length < 5).ToListAsync();

        // The result of ToLower() or ToUpper() can feed IsOneOf()
        var either = await session.Query<Target>().Where(x => x.String.ToLower().IsOneOf("red", "green"))
            .ToListAsync();
    }
    #endregion

    // Covered by Operators/compare_to_operator.cs and Operators/string_compare_operator.cs
    #region sample_comparing_strings
    public async Task comparing_strings(IQuerySession session)
    {
        // string.Compare() and CompareTo() compared with 0 become >, <, >= or <= on the text
        var afterCherry = await session.Query<Target>().Where(x => string.Compare(x.String, "Cherry") > 0)
            .ToListAsync();
        var upToBanana = await session.Query<Target>().Where(x => x.String.CompareTo("Banana") <= 0)
            .ToListAsync();
    }
    #endregion

    // Covered by Operators/regex_is_match.cs
    #region sample_regex_is_match_in_a_child_collection
    public async Task regex_is_match_in_a_child_collection(IQuerySession session)
    {
        // jsonb_path_exists(d.data, '$.Lines[*] ? (@.ItemName like_regex "...")')
        var orders = await session.Query<JsonPathOrder>()
            .Where(x => x.Lines.Any(l => Regex.IsMatch(l.ItemName, "^item-1\\d$")))
            .ToListAsync();
    }
    #endregion
}
