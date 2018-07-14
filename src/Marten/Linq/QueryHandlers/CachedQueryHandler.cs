using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Compiled;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    internal class CachedQueryHandler<T> : IQueryHandler<T>
    {
        private readonly object _model;
        private readonly NpgsqlCommand _template;
        private readonly IQueryHandler<T> _handler;
        private readonly IDbParameterSetter[] _setters;

        public CachedQueryHandler(object model, NpgsqlCommand template, IQueryHandler<T> handler, IDbParameterSetter[] setters)
        {
            _model = model;
            _template = template;
            _handler = handler;
            _setters = setters;
        }

        public Type SourceType => _handler.SourceType;

        public void ConfigureCommand(CommandBuilder builder)
        {
            var sql = _template.CommandText;
            for (var i = 0; i < _setters.Length && i < _template.Parameters.Count; i++)
            {
                var param = _setters[i].AddParameter(_model, builder);

                if (param.Value is Enum)
                {
                    param.Value = (int)param.Value;
                }

                param.NpgsqlDbType = _template.Parameters[i].NpgsqlDbType;

                sql = sql.Replace(":" + _template.Parameters[i].ParameterName, ":" + param.ParameterName);
            }

            builder.Append(sql);
        }

        public T Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return _handler.Handle(reader, map, stats);
        }

        public Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return _handler.HandleAsync(reader, map, stats, token);
        }
    }
}