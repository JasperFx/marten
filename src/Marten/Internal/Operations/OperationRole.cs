namespace Marten.Internal.Operations
{
    public enum OperationRole
    {
        Upsert,
        Insert,
        Update,
        Deletion,
        Patch,
        Other,
        Events
    }
}
