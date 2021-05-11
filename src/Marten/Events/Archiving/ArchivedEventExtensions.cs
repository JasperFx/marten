namespace Marten.Events.Archiving
{
    public static class ArchivedEventExtensions
    {
        /// <summary>
        /// Query for events regardless of whether they are marked
        /// as archived or not
        /// </summary>
        /// <param name="event"></param>
        /// <returns></returns>
        public static bool MaybeArchived(this IEvent @event)
        {
            return true;
        }
    }
}