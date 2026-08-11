using System;
using System.Diagnostics;
using JasperFx.Core.Reflection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DaemonTests.TestingSupport;

public class TestLogger<T>: ILogger<T>, IDisposable
{
    private readonly ITestOutputHelper _output;

    public TestLogger(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
        Func<TState, Exception, string> formatter)
    {
        // Test output intentionally suppressed to keep CI fast; the ITestOutputHelper
        // dependency is retained so existing call sites compile unchanged.
        var message = $"{typeof(T).NameInCode()}/{logLevel}: {formatter(state, exception)}";
        Debug.WriteLine(message);

        if (exception != null)
        {
            Debug.WriteLine(exception);
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        return this;
    }


    public void Dispose()
    {
        // Nothing
    }
}
