using System;
using Npgsql;
#nullable enable
namespace Marten
{
    internal class LambdaConnectionFactory: IConnectionFactory
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
