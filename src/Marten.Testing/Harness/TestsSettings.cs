using System;
using Marten.Services.Json;

namespace Marten.Testing.Harness
{
    public class TestsSettings
    {
        private static SerializerType? serializerType;

        // 9.0: force-fire the Marten.Newtonsoft module initializer so its
        // SerializerFactory.NewtonsoftFactory + StringExtensions resolver registrations
        // are in place before any test creates a JsonNetSerializer. typeof(...) alone
        // is not enough — module initializers fire when a method in the assembly is
        // JIT-compiled, not when a type is referenced. RunModuleConstructor explicitly
        // runs the module's static constructor (which is what [ModuleInitializer] hooks).
        static TestsSettings()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
                typeof(Marten.Newtonsoft.MartenNewtonsoftExtensions).Module.ModuleHandle);
        }

        public static SerializerType SerializerType
        {
            get
            {
                if (serializerType.HasValue)
                    return serializerType.Value;

                var defaultSerializerEnv = Environment.GetEnvironmentVariable("DEFAULT_SERIALIZER");

                serializerType = Enum.TryParse(defaultSerializerEnv, out SerializerType parsedSerializerType)
                    ? parsedSerializerType
                    : SerializerType.Newtonsoft;

                return serializerType.Value;
            }
        }

        private static bool? useClosedShapeStorage;

        /// <summary>
        /// Reads <c>MARTEN_USE_CLOSED_SHAPE_STORAGE</c>. When <c>true</c>, the
        /// harness contexts flip <c>StoreOptions.Events.UseClosedShapeStorage</c>
        /// on after the test's own <c>configure</c> callback runs — so we can
        /// run the whole event-sourcing suite under the closed-shape adapter
        /// (#4417 / #4418) without touching the individual tests.
        /// </summary>
        public static bool UseClosedShapeStorage
        {
            get
            {
                if (useClosedShapeStorage.HasValue)
                    return useClosedShapeStorage.Value;

                var env = Environment.GetEnvironmentVariable("MARTEN_USE_CLOSED_SHAPE_STORAGE");
                useClosedShapeStorage = string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
                return useClosedShapeStorage.Value;
            }
        }
    }
}
