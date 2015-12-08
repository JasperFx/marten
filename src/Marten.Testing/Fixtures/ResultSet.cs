using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;

namespace Marten.Testing.Fixtures
{
    public class ResultSet
    {
        private readonly string[] _names;

        public ResultSet(string names) : this((string[]) names.ToDelimitedArray())
        {
            
        }

        protected bool Equals(ResultSet other)
        {
            return _names.SequenceEqual(other._names);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ResultSet) obj);
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }

        public ResultSet(string[] names)
        {
            _names = names;
        }

        public override string ToString()
        {
            return _names.OrderBy(x => x).Join(", ");
        }
    }
}