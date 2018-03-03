using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
    // SAMPLE: IFeatureSchema
    /// <summary>
    /// Defines the database objects for a named feature within your
    /// Marten application
    /// </summary>
    public interface IFeatureSchema
    {
        /// <summary>
        /// Any document or feature types that this feature depends on. Used
        /// to intelligently order the creation and scripting of database
        /// schema objects
        /// </summary>
        /// <returns></returns>
        IEnumerable<Type> DependentTypes();
        
        /// <summary>
        /// Should this feature be active based on the current options? 
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        bool IsActive(StoreOptions options);
        
        /// <summary>
        /// All the schema objects in this feature
        /// </summary>
        ISchemaObject[] Objects { get; }
        
        /// <summary>
        /// Identifier by type for this feature. Used along with the DependentTypes()
        /// collection to control the proper ordering of object creation or scripting
        /// </summary>
        Type StorageType { get; }

        /// <summary>
        /// Really just the filename when the SQL is exported
        /// </summary>
        string Identifier { get; }

        /// <summary>
        /// Write any permission SQL when this feature is exported to a SQL
        /// file 
        /// </summary>
        /// <param name="rules"></param>
        /// <param name="writer"></param>
        void WritePermissions(DdlRules rules, StringWriter writer);
    }
    // ENDSAMPLE


    
    /// <summary>
    /// Base class for easier creation of custom IFeatureSchema objects
    /// </summary>
    public abstract class FeatureSchemaBase : IFeatureSchema
    {
        public string Identifier { get; }
        public StoreOptions Options { get; }

        protected FeatureSchemaBase(string identifier, StoreOptions options)
        {
            Identifier = identifier;
            Options = options;
        }

        public virtual IEnumerable<Type> DependentTypes()
        {
            return new Type[0];
        }

        public virtual bool IsActive(StoreOptions options)
        {
            return true;
        }

        protected abstract IEnumerable<ISchemaObject> schemaObjects();

        public ISchemaObject[] Objects => schemaObjects().ToArray();

        public virtual Type StorageType => GetType();

        public virtual void WritePermissions(DdlRules rules, StringWriter writer)
        {
            // Nothing
        }
    }

    public static class FeatureSchemaExtensions
    {
        public static void AssertValidNames(this IFeatureSchema schema, StoreOptions options)
        {
            foreach (var objectName in schema.Objects.SelectMany(x => x.AllNames()))
            {
                options.AssertValidIdentifier(objectName.Name);
            }
        }

        public static string ToDDL(this ISchemaObject @object, DdlRules rules)
        {
            var writer = new StringWriter();
            @object.Write(rules, writer);

            return writer.ToString();
        }

        public static void Write(this IFeatureSchema schema, DdlRules rules, StringWriter writer)
        {
            foreach (var schemaObject in schema.Objects)
            {
                schemaObject.Write(rules, writer);
            }

            schema.WritePermissions(rules, writer);
        }

        public static void WriteDropStatements(this IFeatureSchema schema, DdlRules rules, StringWriter writer)
        {
            foreach (var schemaObject in schema.Objects)
            {
                schemaObject.WriteDropStatement(rules, writer);
            }
        }

        public static void RemoveAllObjects(this IFeatureSchema schema, DdlRules rules, NpgsqlConnection conn)
        {
            var writer = new StringWriter();
            schema.WriteDropStatements(rules, writer);

            var sql = writer.ToString();
            var cmd = conn.CreateCommand(sql);

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                throw new MartenCommandException(cmd, e);
            }
        }
    }
}