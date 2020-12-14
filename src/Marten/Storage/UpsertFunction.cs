using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage.Metadata;
using Marten.Util;

namespace Marten.Storage
{
    internal class UpsertFunction: Function
    {
        protected readonly DocumentMapping _mapping;
        private readonly bool _disableConcurrency;
        protected readonly string _primaryKeyConstraintName;
        protected readonly DbObjectName _tableName;
        protected readonly string _tenantWhereClause;
        protected readonly string _andTenantWhereClause;

        public readonly IList<UpsertArgument> Arguments = new List<UpsertArgument>();

        public UpsertFunction(DocumentMapping mapping, DbObjectName identifier = null, bool disableConcurrency = false) : base(identifier ?? mapping.UpsertFunction)
        {
            _mapping = mapping;
            _disableConcurrency = disableConcurrency;
            if (mapping == null)
                throw new ArgumentNullException(nameof(mapping));

            _tableName = mapping.TableName;

            // TODO -- it'd be nice to not need this here.
            var table = new DocumentTable(mapping);
            if (table.PrimaryKeys.Count > 1)
            {
                _primaryKeyConstraintName = mapping.TableName.Name + "_pkey";
            }
            else
            {
                _primaryKeyConstraintName = "pk_" + mapping.TableName.Name;
            }

            var idType = mapping.IdMember.GetMemberType();
            var pgIdType = TypeMappings.GetPgType(idType, mapping.EnumStorage);

            Arguments.Add(new UpsertArgument
            {
                Arg = "docId",
                PostgresType = pgIdType,
                Column = "id",
                Members = new[] { mapping.IdMember }
            });

            Arguments.Add(new DocJsonBodyArgument());

            Arguments.AddRange(mapping.DuplicatedFields.Where(x => !x.OnlyForSearching).Select(x => x.UpsertArgument));

            // TODO -- see the columns below
            if (mapping.Metadata.Version.Enabled) Arguments.Add(new VersionArgument());

            if (mapping.Metadata.DotNetType.Enabled) Arguments.Add(new DotNetTypeArgument());

            AddIfActive(mapping.Metadata.CorrelationId);
            AddIfActive(mapping.Metadata.CausationId);
            AddIfActive(mapping.Metadata.LastModifiedBy);
            AddIfActive(mapping.Metadata.Headers);


            if (mapping.IsHierarchy())
            {
                Arguments.Add(new DocTypeArgument());
            }

            if (mapping.UseOptimisticConcurrency)
            {
                Arguments.Add(new CurrentVersionArgument());
            }

            if (mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                Arguments.Add(new TenantIdArgument());
                _tenantWhereClause = $"{_tableName.QualifiedName}.{TenantIdColumn.Name} = {TenantIdArgument.ArgName}";
                _andTenantWhereClause = $" and {_tenantWhereClause}";
            }
        }

        public void AddIfActive(MetadataColumn column)
        {
            if (column.Enabled)
            {
                Arguments.Add(column.ToArgument());
            }
        }

        public override void Write(DdlRules rules, StringWriter writer)
        {
            // TODO -- this code could be a lot cleaner! The metadata made it go bad

            var ordered = OrderedArguments();

            var argList = ordered.Select(x => x.ArgumentDeclaration()).Join(", ");


            var systemUpdates = _mapping.Metadata.LastModified.Enabled ? new string[] { $"{SchemaConstants.LastModifiedColumn} = transaction_timestamp()" } : new string[0];
            var updates = ordered.Where(x => x.Column != "id" && x.Column.IsNotEmpty())
                .Select(x => $"\"{x.Column}\" = {x.Arg}").Concat(systemUpdates).Join(", ");

            var insertColumns = ordered.Where(x => x.Column.IsNotEmpty()).Select(x => $"\"{x.Column}\"").ToList();

            if (_mapping.Metadata.LastModified.Enabled)
            {
                insertColumns.Add(SchemaConstants.LastModifiedColumn );
            }

            var inserts = insertColumns.Join(", ");

            var valueListColumns = ordered.Where(x => x.Column.IsNotEmpty()).Select(x => x.Arg).ToList();
            if (_mapping.Metadata.LastModified.Enabled)
            {
                valueListColumns.Add("transaction_timestamp()");
            }

            var valueList = valueListColumns.Join(", ");

            var whereClauses = new List<string>();

            if (Arguments.Any(x => x is CurrentVersionArgument) && !_disableConcurrency)
            {
                whereClauses.Add($"{_tableName.QualifiedName}.{SchemaConstants.VersionColumn} = current_version");
            }

            if (Arguments.Any(x => x is TenantIdArgument))
            {
                whereClauses.Add(_tenantWhereClause);
            }

            if (whereClauses.Any())
            {
                updates += " where " + whereClauses.Join(" and ");
            }

            var securityDeclaration = rules.UpsertRights == SecurityRights.Invoker
                ? "SECURITY INVOKER"
                : "SECURITY DEFINER";

            writeFunction(writer, argList, securityDeclaration, inserts, valueList, updates);
        }

        protected virtual void writeFunction(StringWriter writer, string argList, string securityDeclaration, string inserts,
            string valueList, string updates)
        {
            if (_mapping.Metadata.Version.Enabled)
            {
                writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql {
                        securityDeclaration
                    } AS $function$
DECLARE
  final_version uuid;
BEGIN
INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})
  ON CONFLICT ON CONSTRAINT {_primaryKeyConstraintName}
  DO UPDATE SET {updates};

  SELECT mt_version FROM {_tableName.QualifiedName} into final_version WHERE id = docId {_andTenantWhereClause};
  RETURN final_version;
END;
$function$;
");
            }
            else
            {
                writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql {
                        securityDeclaration
                    } AS $function$
DECLARE
  final_version uuid;
BEGIN
INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})
  ON CONFLICT ON CONSTRAINT {_primaryKeyConstraintName}
  DO UPDATE SET {updates};

  RETURN '{Guid.Empty}';
END;
$function$;
");
            }



        }

        public UpsertArgument[] OrderedArguments()
        {
            return Arguments.OrderBy(x => x.Arg).ToArray();
        }

        protected override string toDropSql()
        {
            var argList = OrderedArguments().Select(x => x.PostgresType).Join(", ");
            var dropSql = $"drop function if exists {Identifier.QualifiedName}({argList});";
            return dropSql;
        }

        public void WriteTemplate(DdlRules rules, DdlTemplate template, StringWriter writer)
        {
            var body = ToBody(rules);
            body.WriteTemplate(template, writer);
        }
    }
}
