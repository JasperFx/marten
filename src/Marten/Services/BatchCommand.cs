using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Services
{
    public interface IBatchCommand
    {
        NpgsqlParameter AddParameter(object value, NpgsqlDbType dbType);
        ISerializer Serializer { get; }
        NpgsqlCommand Command { get; }
    }

    public class BatchCommand : IBatchCommand
    {
        public ISerializer Serializer { get; }
        private int _counter = 0;
        private readonly IList<ICall> _calls = new List<ICall>();
        private readonly IList<ICallback> _callbacks = new List<ICallback>();

        public BatchCommand(ISerializer serializer)
        {
            Serializer = serializer;
        }

        public NpgsqlCommand Command { get; } = new NpgsqlCommand();

        public int Count => _calls.Count;

        public NpgsqlParameter AddParameter(object value, NpgsqlDbType dbType)
        {
            var name = "p" + _counter++;
            var param = Command.AddParameter(name, value);
            param.NpgsqlDbType = dbType;

            return param;
        }

        public IList<ICallback> Callbacks => _callbacks;

        public NpgsqlCommand BuildCommand()
        {
            var builder = new StringBuilder();
            _calls.Each(x =>
            {
                x.WriteToSql(builder);
                builder.Append(";");
            });

            Command.CommandText = builder.ToString();

            return Command;
        }

        public void AddCall(ICall call, ICallback callback = null)
        {
            _calls.Add(call);
            _callbacks.Add(callback);
        }

        public SprocCall Sproc(FunctionName function, ICallback callback = null)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));

            var call = new SprocCall(this, function);
            AddCall(call, callback);

            return call;
        }

        public bool HasCallbacks()
        {
            return _callbacks.Any(x => x != null);
        }

    }
}