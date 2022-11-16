namespace Marten.Storage
{
    public enum TenancyStyle
    {
        /// <summary>
        /// No multi-tenancy, the default mode
        /// </summary>
        Single,

        /// <summary>
        /// Multi-tenanted within the same database/schema through a tenant id
        /// </summary>
        Conjoined
    }
}
