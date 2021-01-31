using System;
using System.Collections.Generic;
using Marten.Services.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Allows targeting test at specified serializer type
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    [XunitTestCaseDiscoverer("Marten.Testing.Harness.SerializerTargetedFactDiscoverer", "Marten.Testing")]
    public sealed class SerializerTypeTargetedFact: FactAttribute
    {
        public SerializerType RunFor { get; set; }
    }

    public sealed class SerializerTargetedFactDiscoverer: FactDiscoverer
    {
        private readonly SerializerType serializerType;

        static SerializerTargetedFactDiscoverer()
        {
        }

        public SerializerTargetedFactDiscoverer(IMessageSink diagnosticMessageSink): base(diagnosticMessageSink)
        {
            serializerType = TestsSettings.SerializerType;
        }

        public override IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod,
            IAttributeInfo factAttribute)
        {
            var runForSerializer = factAttribute.GetNamedArgument<SerializerType?>(nameof(SerializerTypeTargetedFact.RunFor));

            if (runForSerializer != null && runForSerializer != serializerType)
            {
                yield return new TestCaseSkippedDueToSerializerSupport($"Test skipped as it cannot be run for {serializerType} ", DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
            }

            yield return base.CreateTestCase(discoveryOptions, testMethod, factAttribute);
        }

        internal sealed class TestCaseSkippedDueToSerializerSupport: XunitTestCase
        {
            [Obsolete("Called by the de-serializer", true)]
            public TestCaseSkippedDueToSerializerSupport()
            {
            }

            public TestCaseSkippedDueToSerializerSupport(string skipReason, IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, object[] testMethodArguments = null) : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
            {
                SkipReason = skipReason;
            }
        }
    }
}
