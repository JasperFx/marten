using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events.TestSupport;
using Marten.Exceptions;
using Shouldly;
using Xunit;

namespace CoreTests;

public class all_exceptions_should_derive_from_MartenException
{
    [Fact]
    public void all_exceptions_types()
    {
        var ignoredTypes = new Type[]
        {
            typeof(ProjectionScenarioException), typeof(MartenException),
            typeof(FastExpressionCompiler.NotSupportedExpressionException),
            // #5514. DefaultTenantUsageDisabledException derives from the JasperFx type that was lifted
            // from Marten's and Polecat's byte-identical copies, so a store-agnostic
            // catch (JasperFx.Events.DefaultTenantUsageDisabledException) actually catches on Marten --
            // it compiled and silently missed before. Multiple inheritance is not available, so the
            // cross-store catch was chosen over MartenException; see the issue for the tradeoff.
            typeof(DefaultTenantUsageDisabledException)
        };

        var exceptionTypes = typeof(MartenException).Assembly.GetTypes()
            .Where(x => x.CanBeCastTo(typeof(Exception)) && !x.CanBeCastTo(typeof(MartenException)) &&
                        !ignoredTypes.Contains(x)).ToList();

        exceptionTypes.ShouldBeEmpty(exceptionTypes.Select(x => x.NameInCode()).Join(", "));

    }
}

/// <summary>
///     #5514. The JasperFx type's own remarks say "stores subclass or type-forward to this", and Polecat did
///     while Marten did not. The failure mode was not an error anywhere — a store-agnostic
///     <c>catch (JasperFx.Events.DefaultTenantUsageDisabledException)</c> compiled, looked like it handled the
///     refusal, and let it through unhandled on Marten only. So the assertion is a catch, not a type
///     relationship: an <c>Assignable</c> check would pass without proving the handler actually runs.
/// </summary>
public class the_default_tenant_refusal_is_catchable_across_stores
{
    [Fact]
    public void a_store_agnostic_catch_handles_martens_exception()
    {
        var handled = false;

        try
        {
            throw new Marten.Exceptions.DefaultTenantUsageDisabledException();
        }
        catch (JasperFx.Events.DefaultTenantUsageDisabledException)
        {
            handled = true;
        }

        handled.ShouldBeTrue();
    }

    /// <summary>
    ///     The lift exists because the two copies were byte-identical. Delegating to the base rather than
    ///     reformatting keeps that true, so an operator-facing message does not quietly change wording.
    /// </summary>
    [Fact]
    public void the_message_is_unchanged_by_the_reparenting()
    {
        new Marten.Exceptions.DefaultTenantUsageDisabledException().Message
            .ShouldBe(new JasperFx.Events.DefaultTenantUsageDisabledException().Message);

        // The single-argument overload appends to the standard prefix rather than replacing it.
        new Marten.Exceptions.DefaultTenantUsageDisabledException("Use a tenanted session.").Message
            .ShouldBe(new JasperFx.Events.DefaultTenantUsageDisabledException("Use a tenanted session.").Message);
    }
}
