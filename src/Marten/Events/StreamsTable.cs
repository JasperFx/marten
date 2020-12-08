using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Parsing;
using Marten.Schema;
using Marten.Storage;
using Marten.Storage.Metadata;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events
{
    public class StreamsTable: Table
    {
        public StreamsTable(EventGraph events) : base(new DbObjectName(events.DatabaseSchemaName, "mt_streams"))
        {
            var idColumn = events.StreamIdentity == StreamIdentity.AsGuid
                ? new StreamTableColumn("id", x => x.Id)
                : new StreamTableColumn("id", x => x.Key);

            if (events.TenancyStyle == TenancyStyle.Conjoined)
            {
                AddPrimaryKeys(new List<TableColumn>
                {
                    idColumn,
                    new TenantIdColumn()
                });
            }
            else
            {
                AddPrimaryKey(idColumn);
            }

            AddColumn(new StreamTableColumn("type", x => x.AggregateTypeName) {Directive = "NULL"});

            AddColumn(new StreamTableColumn("version", x => x.Version) {Directive = "NOT NULL"});

            AddColumn(new StreamTableColumn("timestamp", x => x.Timestamp)
            {
                Type = "timestamptz",
                Directive = "default (now()) NOT NULL",
                Writes = false
            });


            AddColumn("snapshot", "jsonb");
            AddColumn("snapshot_version", "integer");

            AddColumn(new StreamTableColumn("created", x => x.Created)
            {
                Directive = "default (now()) NOT NULL", Writes = false, Type = "timestamptz"
            });

            if (events.TenancyStyle != TenancyStyle.Conjoined)
            {
                AddColumn<TenantIdColumn>();
            }
        }
    }

    internal interface IStreamTableColumn
    {
        void GenerateAppendCode(GeneratedMethod method, int index);

        public abstract void GenerateSelectorCodeAsync(GeneratedMethod method, int index);
        public void GenerateSelectorCodeSync(GeneratedMethod method, int index);

        bool Reads { get; }
        bool Writes { get; }

        string Name { get; }

    }

    internal class StreamTableColumn: TableColumn, IStreamTableColumn
    {
        private readonly Expression<Func<StreamAction, object>> _memberExpression;
        private readonly MemberInfo _member;
        public NpgsqlDbType NpgsqlDbType { get; set; }

        public StreamTableColumn(string name, Expression<Func<StreamAction, object>> memberExpression) : base(name, "varchar")
        {
            _memberExpression = memberExpression;
            _member = FindMembers.Determine(memberExpression).Single();
            var memberType = _member.GetMemberType();
            Type = TypeMappings.GetPgType(memberType, EnumStorage.AsInteger);
            NpgsqlDbType = TypeMappings.ToDbType(memberType);
        }

        public bool Reads { get; set; } = true;
        public bool Writes { get; set; } = true;

        public void GenerateAppendCode(GeneratedMethod method, int index)
        {
            method.SetParameterFromMember(index, _memberExpression);
        }

        public void GenerateSelectorCodeAsync(GeneratedMethod method, int index)
        {
            method.AssignMemberFromReader(null, index, typeof(StreamAction), _member.Name);
        }

        public void GenerateSelectorCodeSync(GeneratedMethod method, int index)
        {
            method.AssignMemberFromReaderAsync(null, index, typeof(StreamAction), _member.Name);
        }
    }






}
