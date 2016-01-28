using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.Serialization;
using Baseline;
using Baseline.Reflection;
using Marten.Generation;
using Marten.Schema.Sequences;
using Marten.Util;

namespace Marten.Schema
{
    [Serializable]
    public class InvalidDocumentException : Exception
    {
        public InvalidDocumentException(string message) : base(message)
        {
        }

        protected InvalidDocumentException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    public class DocumentMapping
    {
        private readonly ConcurrentDictionary<string, IField> _fields = new ConcurrentDictionary<string, IField>();
        private PropertySearching _propertySearching = PropertySearching.JSON_Locator_Only;

        public DocumentMapping(Type documentType) : this(documentType, new StoreOptions())
        {

        }

        public DocumentMapping(Type documentType, StoreOptions options)
        {
            DocumentType = documentType;

            IdMember = (MemberInfo) documentType.GetProperties().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id"))
                       ?? documentType.GetFields().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id"));

            if (IdMember == null)
            {
                throw new InvalidDocumentException($"Could not determine an 'id/Id' field or property for requested document type {documentType.FullName}");
            }


            assignIdStrategy(documentType, options);

            TableName = TableNameFor(documentType);

            UpsertName = UpsertNameFor(documentType);

            documentType.ForAttribute<MartenAttribute>(att => att.Modify(this));

            documentType.GetProperties().Where(x => TypeMappings.HasTypeMapping(x.PropertyType)).Each(prop =>
            {

                var field = new LateralJoinField(prop);
                _fields[field.MemberName] = field;

                prop.ForAttribute<MartenAttribute>(att => att.Modify(this, prop));
            });

            documentType.GetFields().Where(x => TypeMappings.HasTypeMapping(x.FieldType)).Each(fieldInfo =>
            {
                var field = new LateralJoinField(fieldInfo);
                _fields.AddOrUpdate(field.MemberName, field, (key, f) => f);

                fieldInfo.ForAttribute<MartenAttribute>(att => att.Modify(this, fieldInfo));
            });
        }

        public IndexDefinition AddGinIndexToData()
        {
            var index = AddIndex("data");
            index.Method = IndexMethod.gin;
            index.Expression = "? jsonb_path_ops";

            PropertySearching = Schema.PropertySearching.ContainmentOperator;

            return index;
        }

        public IndexDefinition AddIndex(params string[] columns)
        {
            var index = new IndexDefinition(this, columns);
            Indexes.Add(index);

            return index;
        }


        public IList<IndexDefinition> Indexes { get; } = new List<IndexDefinition>();

        private void assignIdStrategy(Type documentType, StoreOptions options)
        {
            var idType = IdMember.GetMemberType();
            if (idType == typeof (string))
            {
                IdStrategy = new StringIdGeneration();
            }
            else if (idType == typeof (Guid))
            {
                IdStrategy = new GuidIdGeneration();
            }
            else if (idType == typeof (int) || idType == typeof (long))
            {
                IdStrategy = new HiloIdGeneration(documentType, options.HiloSequenceDefaults);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(documentType),
                    $"Marten cannot use the type {idType.FullName} as the Id for a persisted document. Use int, long, Guid, or string");
            }
        }

        public void HiLoSettings(HiloDef hilo)
        {
            if (IdStrategy is HiloIdGeneration)
            {
                IdStrategy = new HiloIdGeneration(DocumentType, hilo);
            }
            else
            {
                throw new InvalidOperationException($"DocumentMapping for {DocumentType.FullName} is using {IdStrategy.GetType().FullName} as its Id strategy so cannot override HiLo sequence configuration");
            }
        }

        public IIdGeneration IdStrategy { get; set; } = new StringIdGeneration();

        public string UpsertName { get; }

        public Type DocumentType { get; }

        public string TableName { get; set; }

        public MemberInfo IdMember { get; set; }

        public PropertySearching PropertySearching
        {
            get { return _propertySearching; }
            set
            {
                _propertySearching = value;

                if (_propertySearching == PropertySearching.JSONB_To_Record)
                {
                    var fields = _fields.Values.Where(x => x.Members.Length == 1).OfType<JsonLocatorField>().ToArray();
                    fields.Each(x => { _fields[x.MemberName] = new LateralJoinField(x.Members.Last()); });
                }
                else
                {
                    var fields = _fields.Values.Where(x => x.Members.Length == 1).OfType<LateralJoinField>().ToArray();
                    fields.Each(x => { _fields[x.MemberName] = new JsonLocatorField(x.Members.Last()); });
                }
            }
        }

        public IEnumerable<DuplicatedField> DuplicatedFields => _fields.Values.OfType<DuplicatedField>();

        public static string TableNameFor(Type documentType)
        {
            return "mt_doc_" + documentType.Name.ToLower();
        }

        public static string UpsertNameFor(Type documentType)
        {
            return "mt_upsert_" + documentType.Name.ToLower();
        }

        public IField FieldFor(MemberInfo member)
        {
            return _fields.GetOrAdd(member.Name, name =>
            {
                return PropertySearching == PropertySearching.JSONB_To_Record
                    ? (IField) new LateralJoinField(member)
                    : new JsonLocatorField(member);
            });
        }

        public IField FieldFor(string memberName)
        {
            return _fields[memberName];
        }

        public TableDefinition ToTable(IDocumentSchema schema) // take in schema so that you
            // can do foreign keys
        {
            // TODO -- blow up if no IdMember or no TableName


            var pgIdType = TypeMappings.GetPgType(IdMember.GetMemberType());
            var table = new TableDefinition(TableName, new TableColumn("id", pgIdType));
            table.Columns.Add(new TableColumn("data", "jsonb") {Directive = "NOT NULL" });

            _fields.Values.OfType<DuplicatedField>().Select(x => x.ToColumn(schema)).Each(x => table.Columns.Add(x));


            return table;
        }


        public static DocumentMapping For<T>()
        {
            return new DocumentMapping(typeof (T));
        }

        public DuplicatedField DuplicateField(string memberName, string pgType = null)
        {
            var field = FieldFor(memberName);
            var duplicate = new DuplicatedField(field.Members);
            if (pgType.IsNotEmpty())
            {
                duplicate.PgType = pgType;
            }

            _fields[memberName] = duplicate;

            return duplicate;
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            if (members.Count() == 1)
            {
                return FieldFor(members.Single());
            }

            var key = members.Select(x => x.Name).Join("");
            return _fields.GetOrAdd(key, _ =>
            {
                return new JsonLocatorField(members.ToArray());
            });
        }

        public IndexDefinition DuplicateField(MemberInfo[] members, string pgType = null)
        {
            var memberName = members.Select(x => x.Name).Join("");

            var duplicatedField = new DuplicatedField(members);
            if (pgType.IsNotEmpty())
            {
                duplicatedField.PgType = pgType;
            }

            _fields[memberName] = duplicatedField;

            return AddIndex(duplicatedField.ColumnName);
        }

        public IEnumerable<IndexDefinition> IndexesFor(string column)
        {
            return Indexes.Where(x => x.Columns.Contains(column));
        }
    }
}