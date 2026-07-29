using System;
using Marten.Services.Json;
using Xunit;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Allows targeting test at specified serializer type
    /// </summary>
    /// <remarks>
    /// See <see cref="SerializerTypeTargetedFact"/> for why the v2 discoverer this used to
    /// carry is gone under xunit v3.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SerializerTypeTargetedTheory: TheoryAttribute
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
