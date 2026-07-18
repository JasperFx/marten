#nullable enable
using JasperFx;
using JasperFx.MultiTenancy;

namespace Marten.Metadata;

/// <summary>
///     Optionally implement this interface on your Marten document
///     types to opt into conjoined tenancy and track the tenant id
///     on the document itself. Derives from the shared
///     <see cref="JasperFx.MultiTenancy.ITenanted"/> marker (jasperfx#531)
///     so the same marker drives conjoined behavior across Marten,
///     Polecat, and Wolverine.
/// </summary>
public interface ITenanted : JasperFx.MultiTenancy.ITenanted
{

}
