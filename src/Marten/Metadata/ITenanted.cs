
#nullable enable
namespace Marten.Metadata
{
    /// <summary>
    /// Optionally implement this interface on your Marten document
    /// types to opt into conjoined tenancy and track the tenant id
    /// on the document itself
    /// </summary>
    public interface ITenanted
    {
        string TenantId { get; set; }
    }
}
