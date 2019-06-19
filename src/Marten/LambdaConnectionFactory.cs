using System;
using Npgsql;

namespace Marten
{
    public class LambdaConnectionFactory: IConnectionFactory
    {
        private readonly Func<NpgsqlConnection> _source;

        public LambdaConnectionFactory(Func<NpgsqlConnection> source)
        {
            _source = source;
        }

        public NpgsqlConnection Create()
        {
            return _source();
        }
    }
}
