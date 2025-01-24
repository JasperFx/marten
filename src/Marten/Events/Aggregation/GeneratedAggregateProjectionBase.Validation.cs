using System.Collections.Generic;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.Aggregation;

public abstract partial class GeneratedAggregateProjectionBase<T>
{
    protected virtual void specialAssertValid() { }

    protected abstract IEnumerable<string> validateDocumentIdentity(StoreOptions options, DocumentMapping mapping);

    internal override IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        var mapping = options.Storage.FindMapping(typeof(T)).Root.As<DocumentMapping>();

        foreach (var p in validateDocumentIdentity(options, mapping)) yield return p;

        if (options.Events.TenancyStyle != mapping.TenancyStyle
            && (options.Events.TenancyStyle == TenancyStyle.Single
                || options.Events is
                    { TenancyStyle: TenancyStyle.Conjoined, EnableGlobalProjectionsForConjoinedTenancy: false }
            && Lifecycle != ProjectionLifecycle.Live)
           )
        {
            yield return
                $"Tenancy storage style mismatch between the events ({options.Events.TenancyStyle}) and the aggregate type {typeof(T).FullNameInCode()} ({mapping.TenancyStyle})";
        }

        if (mapping.DeleteStyle == DeleteStyle.SoftDelete)
        {
            yield return
                "AggregateProjection cannot support aggregates that are soft-deleted";
        }
    }

    internal override void AssembleAndAssertValidity()
    {
        if (_applyMethods.IsEmpty() && _createMethods.IsEmpty())
        {
            throw new InvalidProjectionException(
                $"AggregateProjection for {typeof(T).FullNameInCode()} has no valid create or apply operations");
        }

        var invalidMethods =
            MethodCollection.FindInvalidMethods(GetType(), _applyMethods, _createMethods, _shouldDeleteMethods)
                .Where(x => x.Method.Name != nameof(IAggregateProjectionWithSideEffects<string>.RaiseSideEffects));

        if (invalidMethods.Any())
        {
            throw new InvalidProjectionException(this, invalidMethods);
        }

        specialAssertValid();

        var eventTypes = determineEventTypes();
        IncludedEventTypes.Fill(eventTypes);
    }
}
