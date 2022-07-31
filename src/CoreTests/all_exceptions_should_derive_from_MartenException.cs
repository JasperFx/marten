using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using Marten.Events.TestSupport;
using Marten.Exceptions;
using Shouldly;
using Xunit;

namespace CoreTests
{
    public class all_exceptions_should_derive_from_MartenException
    {
        [Fact]
        public void all_exceptions_types()
        {
            var ignoredTypes = new Type[] { typeof(ProjectionScenarioException), typeof(MartenException), typeof(FastExpressionCompiler.NotSupportedExpressionException) };

            var exceptionTypes = typeof(MartenException).Assembly.GetTypes()
                .Where(x => x.CanBeCastTo(typeof(Exception)) && !x.CanBeCastTo(typeof(MartenException)) &&
                            !ignoredTypes.Contains(x)).ToList();

            exceptionTypes.ShouldBeEmpty(exceptionTypes.Select(x => x.NameInCode()).Join(", "));

        }
    }
}
