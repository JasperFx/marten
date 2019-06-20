using System;
using System.Collections.Generic;
using Npgsql;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Allows targeting test at specified minimum and/or maximum version of PG
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    [XunitTestCaseDiscoverer("Marten.Testing.Harness.PgVersionTargetedFactDiscoverer", "Marten.Testing")]
    public sealed class PgVersionTargetedFact: FactAttribute
    {
        public string MinimumVersion { get; set; }
        public string MaximumVersion { get; set; }
    }

    public sealed class PgVersionTargetedFactDiscoverer: FactDiscoverer
    {
        private static readonly Version Version;

        static PgVersionTargetedFactDiscoverer()
        {
            // PG version does not change during test run so we can do static ctor
            using (var c = new NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                c.Open();
                Version = c.PostgreSqlVersion;
                c.Close();
            }
        }

        public PgVersionTargetedFactDiscoverer(IMessageSink diagnosticMessageSink): base(diagnosticMessageSink)
        {
        }

        public override IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod,
            IAttributeInfo factAttribute)
        {
            var minimumVersion = factAttribute.GetNamedArgument<string>(nameof(PgVersionTargetedFact.MinimumVersion));
            var maximumVersion = factAttribute.GetNamedArgument<string>(nameof(PgVersionTargetedFact.MaximumVersion));

            if (minimumVersion != null && Version.TryParse(minimumVersion, out var minVersion) && Version < minVersion)
            {
                yield return new TestCaseSkippedDueToVersion($"Minimum required PG version {minimumVersion} is higher than {Version}", DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
            }
            if (maximumVersion != null && Version.TryParse(maximumVersion, out var maxVersion) && Version > maxVersion)
            {
                yield return new TestCaseSkippedDueToVersion($"Maximum allowed PG version {maximumVersion} is higher than {Version}", DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
            }
            
            yield return base.CreateTestCase(discoveryOptions, testMethod, factAttribute);
        }

        internal sealed class TestCaseSkippedDueToVersion: XunitTestCase
        {
            [Obsolete("Called by the de-serializer", true)]
            public TestCaseSkippedDueToVersion()
            {
            }

            public TestCaseSkippedDueToVersion(string skipReason, IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, object[] testMethodArguments = null) : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
            {
                SkipReason = skipReason;
            }
        }
    }
}
