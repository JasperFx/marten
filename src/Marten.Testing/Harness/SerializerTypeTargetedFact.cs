using System;
using Marten.Services.Json;
using Xunit;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Allows targeting test at specified serializer type
    /// </summary>
    /// <remarks>
    /// xunit v3 removed the v2 discoverer model this was built on (IAttributeInfo,
    /// IMessageSink-constructed discoverers, XunitTestCase). None of it is needed any more:
    /// assigning <see cref="FactAttribute.Skip"/> from the <see cref="RunFor"/> setter is
    /// enough, because attribute property setters run while the attribute is being
    /// constructed, which is before xunit reads Skip.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SerializerTypeTargetedFact: FactAttribute
    {
        private SerializerType _runFor;

        public SerializerType RunFor
        {
            get => _runFor;
            set
            {
                _runFor = value;
                if (value != TestsSettings.SerializerType)
                {
                    Skip = $"Test skipped as it cannot be run for {TestsSettings.SerializerType} ";
                }
            }
        }
    }
}
