namespace Marten;

public enum DocumentTracking
{
    None,
    IdentityOnly,
    DirtyTracking,

    /// <summary>
    ///     Refers to a query only session type, invalid inside of OpenSession()
    /// </summary>
    QueryOnly
}
