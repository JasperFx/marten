using System;
using System.Diagnostics;
using LamarCodeGeneration;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing.TestingSupport
{
    public class TestLogger<T> : ILogger<T>, IDisposable
    {
        private readonly ITestOutputHelper _output;

        public TestLogger(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = $"{typeof(T).NameInCode()}/{logLevel}: {formatter(state, exception)}";
            Debug.WriteLine(message);
            _output.WriteLine(message);
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
}
