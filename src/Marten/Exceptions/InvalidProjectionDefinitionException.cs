using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LamarCodeGeneration;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;

namespace Marten.Exceptions
{
    public class InvalidProjectionDefinitionException : Exception
    {

        internal InvalidProjectionDefinitionException(IValidatedProjection projection, IEnumerable<MethodSlot> invalidMethods) : base(ToMessage(projection, invalidMethods))
        {
            InvalidMethods = invalidMethods.ToArray();
        }

        private static string ToMessage(IValidatedProjection projection, IEnumerable<MethodSlot> invalidMethods)
        {
            var writer = new StringWriter();
            writer.WriteLine($"Projection {projection.GetType().FullNameInCode()} has validation errors:");
            foreach (var slot in invalidMethods)
            {
                writer.WriteLine(slot.Signature());
                foreach (var error in slot.Errors)
                {
                    writer.WriteLine(" - " + error);
                }
            }

            return writer.ToString();
        }

        public MethodSlot[] InvalidMethods { get; }
    }
}
