#nullable enable
using JasperFx.Core;
using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration;

/// <summary>
/// Implemented by a select clause whose select list can carry bound command parameters, such as a
/// <c>Select()</c> projection with a non-string constant (#4954). <see cref="ISelectClause.SelectFields" />
/// returns the list as text, which keeps a parameter's placeholder but drops its value, so a clause
/// wrapping another one writes the inner list through <see cref="ApplySelectFields" /> instead.
/// </summary>
internal interface ISelectFieldsFragment
{
    /// <summary>
    /// Write the same comma-separated select list that <see cref="ISelectClause.SelectFields" /> describes,
    /// binding any parameters it holds to <paramref name="sql" />.
    /// </summary>
    void ApplySelectFields(ICommandBuilder sql);
}

internal static class SelectFieldsFragmentExtensions
{
    /// <summary>
    /// Write <paramref name="clause" />'s select list into the command being built, keeping its
    /// parameters bound when it has any.
    /// </summary>
    public static void ApplySelectFields(this ISelectClause clause, ICommandBuilder sql)
    {
        if (clause is ISelectFieldsFragment fragment)
        {
            fragment.ApplySelectFields(sql);
            return;
        }

        sql.Append(clause.SelectFields().Join(", "));
    }
}
