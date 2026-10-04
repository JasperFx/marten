using Marten.Internal.CompiledQueries;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5573 / weasel#675. <see cref="CompiledQueryPlan" /> implements
/// <see cref="ICommandBuilder" />, whose <see cref="ICommandBuilder.ParameterCount" /> lets a
/// fragment rendering a value list decide against the provider's per-command parameter budget
/// (<c>Migrator.MaxParametersPerCommand</c>).
/// </summary>
/// <remarks>
/// The member ships with a default implementation returning
/// <see cref="ICommandBuilder.UnknownParameterCount" /> (-1), so that a Marten published before
/// Weasel 9.40.0 keeps loading against it. That default is the reason these assertions are worth
/// having: drop Marten's explicit implementation and nothing fails to compile and nothing throws —
/// every caller just silently starts being told "no information". So assert on the number, and
/// assert it is not the sentinel.
/// </remarks>
public class compiled_query_plan_parameter_count
{
    private static ICommandBuilder freshPlan()
    {
        var plan = new CompiledQueryPlan(typeof(object), typeof(object));
        ICommandBuilder builder = plan;

        // _current is only created by the first append, which is also true of LastParameterName
        // next to it. Every real caller is mid-render by the time it asks.
        builder.Append("select 1 where ");

        return builder;
    }

    [Fact]
    public void counts_the_parameters_on_the_current_command()
    {
        var builder = freshPlan();
        builder.ParameterCount.ShouldBe(0);

        builder.AppendParameter("first");
        builder.ParameterCount.ShouldBe(1);

        builder.AppendParameter("second");
        builder.AppendParameter("third");
        builder.ParameterCount.ShouldBe(3);
    }

    [Fact]
    public void does_not_report_the_unknown_sentinel()
    {
        var builder = freshPlan();
        builder.AppendParameter("only");

        builder.ParameterCount.ShouldNotBe(ICommandBuilder.UnknownParameterCount);
        builder.ParameterCount.ShouldBe(1);
    }

    [Fact]
    public void resets_at_each_new_command()
    {
        var builder = freshPlan();
        builder.AppendParameter("first");
        builder.AppendParameter("second");
        builder.ParameterCount.ShouldBe(2);

        // The budget being compared against is per command, so the count has to follow the command
        // the plan is recording into rather than accumulate across the batch.
        builder.StartNewCommand();
        builder.ParameterCount.ShouldBe(0);

        builder.AppendParameter("third");
        builder.ParameterCount.ShouldBe(1);
    }
}
