namespace Marten.Storage
{
    public enum SchemaPatchDifference
    {
        None = 3,
        Create = 1,
        Update = 2,
        Invalid = 0
    }
}