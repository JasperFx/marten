using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Marten.Util;
using Npgsql;
using TypeExtensions = LamarCodeGeneration.Util.TypeExtensions;

namespace Marten.Linq.SqlGeneration
{

    internal class JsonSelectClause: ISelectClause
    {
        private readonly Type _sourceType;

        public JsonSelectClause(ISelectClause parent)
        {
            _sourceType = parent.SelectedType;
            FromObject = parent.FromObject;

            if (parent is IScalarSelectClause c)
            {
                SelectionText = $"select {c.FieldName} from ";
            }
        }

        public Type SelectedType => typeof(string);

        public string FromObject { get; }

        public string SelectionText { get; set; } = "select d.data from ";


        public void WriteSelectClause(CommandBuilder sql)
        {
            sql.Append(SelectionText);
            sql.Append(FromObject);
            sql.Append(" as d");
        }

        public string[] SelectFields()
        {
            return new string[] {"data"};
        }

        public ISelector BuildSelector(IMartenSession session)
        {
            return LinqConstants.StringValueSelector;
        }

        public IQueryHandler<T> BuildHandler<T>(IMartenSession session, Statement topStatement,
            Statement currentStatement)
        {
            if (currentStatement.Limit == 1)
            {
                return (IQueryHandler<T>)new QueryHandlers.OneResultHandler<string>(topStatement, LinqConstants.StringValueSelector, true, false);
            }

            return (IQueryHandler<T>) new JsonArrayHandler(topStatement, _sourceType);
        }

        public ISelectClause UseStatistics(QueryStatistics statistics)
        {
            throw new System.NotSupportedException("Marten does not yet support the usage of QueryStatistics combined with JSON queries");
        }
    }

    public class JsonArrayHandler: IQueryHandler<string>, IQueryHandler<IReadOnlyList<string>>, IQueryHandler<IEnumerable<string>>, ISelector<string>
    {
        private readonly Statement _statement;
        private string _arrayPrefix;
        private string _arraySuffix;

        public JsonArrayHandler(Statement statement, Type sourceType)
        {
            _statement = statement;
            if (sourceType.IsSimple())
            {
                _arrayPrefix = "[{";
                _arraySuffix = "}]";
            }
            else
            {
                _arrayPrefix = "[";
                _arraySuffix = "]";
            }
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _statement.Configure(builder);
        }

        public string Handle(DbDataReader reader, IMartenSession session)
        {
            // TODO -- figure out better, more efficient ways to do this
            var builder = new StringWriter();

            builder.Write(_arrayPrefix);

            if (reader.Read())
            {
                using var text = reader.GetStream(0);

                builder.Write(text.GetStreamReader().ReadToEnd());
            }

            while (reader.Read())
            {
                using var text = reader.GetStream(0);
                builder.Write(',');
                builder.Write(text.GetStreamReader().ReadToEnd());
            }

            builder.Write(_arraySuffix);

            return builder.ToString();
        }

        Task<IEnumerable<string>> IQueryHandler<IEnumerable<string>>.HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            IQueryHandler<IEnumerable<string>> inner = new ListQueryHandler<string>(null, this);
            return inner.HandleAsync(reader, session, token);
        }

        IEnumerable<string> IQueryHandler<IEnumerable<string>>.Handle(DbDataReader reader, IMartenSession session)
        {
            IQueryHandler<IEnumerable<string>> inner = new ListQueryHandler<string>(null, this);
            return inner.Handle(reader, session);
        }

        Task<IReadOnlyList<string>> IQueryHandler<IReadOnlyList<string>>.HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var inner = new ListQueryHandler<string>(null, this);
            return inner.HandleAsync(reader, session, token);
        }

        IReadOnlyList<string> IQueryHandler<IReadOnlyList<string>>.Handle(DbDataReader reader, IMartenSession session)
        {
            var inner = new ListQueryHandler<string>(null, this);
            return inner.Handle(reader, session);
        }

        public async Task<string> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            // TODO -- figure out better, more efficient ways to do this
            var builder = new StringWriter();

            await builder.WriteAsync(_arrayPrefix);

            if (await reader.ReadAsync(token))
            {
                using var text = await reader.As<NpgsqlDataReader>().GetStreamAsync(0, token);

                await builder.WriteAsync(await text.GetStreamReader().ReadToEndAsync());
            }

            while (await reader.ReadAsync(token))
            {
                using var text = await reader.As<NpgsqlDataReader>().GetStreamAsync(0, token);
                await builder.WriteAsync(',');
                await builder.WriteAsync(await text.GetStreamReader().ReadToEndAsync());
            }

            await builder.WriteAsync(_arraySuffix);

            return builder.ToString();
        }

        string ISelector<string>.Resolve(DbDataReader reader)
        {
            return reader.GetFieldValue<string>(0);
        }

        Task<string> ISelector<string>.ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            return reader.GetFieldValueAsync<string>(0, token);
        }
    }
}
