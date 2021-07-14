namespace Marten.Schema
{
    internal class SchemaConstants
    {
        public const string BaseAlias = "BASE";
        public const string TablePrefix = "mt_doc_";
        public const string UpsertPrefix = "mt_upsert_";
        public const string InsertPrefix = "mt_insert_";
        public const string UpdatePrefix = "mt_update_";
        public const string OverwritePrefix = "mt_overwrite_";
        public const string DocumentTypeColumn = "mt_doc_type";
        public const string MartenPrefix = "mt_";
        public const string LastModifiedColumn = "mt_last_modified";
        public const string DotNetTypeColumn = "mt_dotnet_type";
        public const string VersionColumn = "mt_version";
        public const string DeletedColumn = "mt_deleted";
        public const string DeletedAtColumn = "mt_deleted_at";

        public const string MartenGeneratedNamespace = "Marten.Generated";

        public const string DefaultSchema = "public";
    }
}
