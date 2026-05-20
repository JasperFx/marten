#nullable enable

namespace Marten.Schema;

/// <summary>
///     A set of initial data to pre-populate a DocumentStore at startup time
///     Users will have to be responsible for not duplicating data.
///     Re-based on the lifted <see cref="JasperFx.IInitialData{TStore}"/>
///     (jasperfx#334 / marten#4524); the Populate(IDocumentStore, CancellationToken)
///     signature is unchanged so existing implementers and MartenActivator are
///     unaffected.
/// </summary>
public interface IInitialData: JasperFx.IInitialData<IDocumentStore>
{
}
