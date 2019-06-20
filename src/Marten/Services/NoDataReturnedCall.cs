namespace Marten.Services
{
    /// <summary>
    /// Marker interface telling Marten not
    /// to advance the results for callbacks
    /// </summary>
    public interface NoDataReturnedCall: IStorageOperation
    {
    }
}
