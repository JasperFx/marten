using System;
using System.Reflection;
using FubuCore.Csv;
using Marten.Generation;
using Marten.Util;

namespace Marten.Schema
{
    public class DocumentMapping
    {
        public DocumentMapping(Type documentType)
        {
            DocumentType = documentType;
        }

        public Type DocumentType { get; private set; }

        public string TableName { get; set; }

        public MemberInfo IdMember { get; set; }

        // LATER?
        public IdStrategy IdStrategy { get; set; }

        public static string TableNameFor(Type documentType)
        {
            return "mt_doc_" + documentType.Name.ToLower();
        }

        public static string UpsertNameFor(Type documentType)
        {
            return "mt_upsert_" + documentType.Name.ToLower();
        }

        public TableDefinition ToTable(IDocumentSchema schema) // take in schema so that you
            // can do foreign keys
        {
            throw new NotImplementedException();
        }

        public void GenerateDocumentStorage(AssemblyGenerator generator)
        {
            throw new NotImplementedException();
        }
    }

    // There would be others for sequences and hilo, etc.
    public interface IdStrategy
    {
    }

    public class AssignGuid : IdStrategy
    {
    }

    public enum DuplicatedFieldRole
    {
        Search,
        ForeignKey
    }

    public class DuplicatedField
    {
        public DuplicatedField(MemberInfo[] memberPath)
        {
            MemberPath = memberPath;
            UpsertArgument = new UpsertArgument();
        }

        /// <summary>
        ///     Because this could be a deeply nested property and maybe even an
        ///     indexer? Or change to MemberInfo[] instead.
        /// </summary>
        public MemberInfo[] MemberPath { get; private set; }

        public string ColumnName { get; set; }

        public DuplicatedFieldRole Role { get; set; } = DuplicatedFieldRole.Search;

        public UpsertArgument UpsertArgument { get; private set; }

        // I say you don't need a ForeignKey 
        public virtual ColumnDefinition ToColumn()
        {
            throw new NotImplementedException();
        }
    }

    public class UpsertArgument
    {
        public string Name { get; set; }
        public string PostgresType { get; set; }
    }
}