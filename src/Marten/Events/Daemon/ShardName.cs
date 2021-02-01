namespace Marten.Events.Daemon
{
    /// <summary>
    /// Identity for a single async shard
    /// </summary>
    public class ShardName
    {
        public const string All = "All";

        public ShardName(string projectionName, string key)
        {
            ProjectionName = projectionName;
            Key = key;
            Identity = $"{projectionName}:{key}";
        }

        public ShardName(string projectionName) : this(projectionName, All)
        {
        }

        /// <summary>
        /// Parent projection name
        /// </summary>
        public string ProjectionName { get; }

        /// <summary>
        /// The identity of the shard within the projection. If there is only
        /// one shard for a projection, this will be "All"
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// {ProjectionName}:{Key}. Single identity string that should be unique within this Marten application
        /// </summary>
        public string Identity { get; }

        public override string ToString()
        {
            return $"{nameof(Identity)}: {Identity}";
        }



        protected bool Equals(ShardName other)
        {
            return Identity == other.Identity;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ShardName) obj);
        }

        public override int GetHashCode()
        {
            return (Identity != null ? Identity.GetHashCode() : 0);
        }
    }
}
