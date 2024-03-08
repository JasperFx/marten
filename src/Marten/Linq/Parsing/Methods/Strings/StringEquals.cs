using System;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods.Strings;

internal class StringEquals: StringComparisonParser
{
    public StringEquals(): base(
        ReflectionHelper.GetMethod<string>(s => s.Equals(string.Empty)),
        ReflectionHelper.GetMethod<string>(s => s.Equals(string.Empty, StringComparison.CurrentCulture)),
        ReflectionHelper.GetMethod(() => string.Equals(string.Empty, string.Empty)),
        ReflectionHelper.GetMethod(() => string.Equals(string.Empty, string.Empty, StringComparison.CurrentCulture)))
    {
    }

    protected override ISqlFragment buildFilter(bool caseInsensitive, IQueryableMember member, CommandParameter value)
    {
        return caseInsensitive
            ? new StringEqualsIgnoreCaseFilter(member, value)
            : new MemberComparisonFilter(member, value, "=");
    }
}

internal class StringEqualsIgnoreCaseFilter : ISqlFragment
{
    public IQueryableMember Member { get; }
    public CommandParameter Value { get; }

    public StringEqualsIgnoreCaseFilter(IQueryableMember member, CommandParameter value)
    {
        Member = member;
        Value = new CommandParameter(StringComparisonParser.EscapeValue(value.Value?.ToString() ?? string.Empty));
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(Member.RawLocator);
        builder.Append(StringComparisonParser.CaseInSensitiveLike);
        Value.Apply(builder);
    }

}
