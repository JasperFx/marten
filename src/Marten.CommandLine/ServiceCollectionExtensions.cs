using Weasel.Core.CommandLine;

namespace Marten.CommandLine;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Oakton environment checks to assert that all the configured database configuration is up to date
    /// See https://jasperfx.github.io/oakton/documentation/hostbuilder/environment/ for more information
    /// </summary>
    /// <param name="expression"></param>
    /// <returns></returns>
    public static MartenServiceCollectionExtensions.MartenConfigurationExpression AddEnvironmentChecks(this MartenServiceCollectionExtensions.MartenConfigurationExpression expression)
    {
        expression.Services.CheckAllWeaselDatabases();
        return expression;
    }
}
