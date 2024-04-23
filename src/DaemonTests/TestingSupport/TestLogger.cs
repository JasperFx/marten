using System;
using System.Diagnostics;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

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
        var message = $"{typeof(T).NameInCode()}/{logLevel}: {formatter(state, exception)}";
        Debug.WriteLine(message);
        _output.WriteLine(message);

        if (exception != null)
        {
            Debug.WriteLine(exception);
            _output.WriteLine(exception.ToString());
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
