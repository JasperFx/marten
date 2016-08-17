using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;

namespace Marten.CLI.Infrastructure
{
    public sealed class InteractiveContext : ICommandContext
    {        
        private readonly List<object> items = new List<object>();

        public string Ask(string parameter, string description = null)
        {
            Console.WriteLine($"{description} >");
            var entry = Console.ReadLine();
            return entry;
        }

        public T Single<T>()
        {
            return items.OfType<T>().Single();
        }

        public IEnumerable<T> All<T>()
        {
            return items.OfType<T>();
        }

        public void Record(object result)
        {
            items.Add(result);
        }

        public void Dispose()
        {
            items.OfType<IDisposable>().Each(x =>
            {
                x.Dispose();
            });
        }
    }
}