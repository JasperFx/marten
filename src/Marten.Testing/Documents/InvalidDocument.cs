using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Marten.Testing.Documents
{
    // this document does not have an identity field
    public class InvalidDocument
    {
        public string Name { get; set; }
    }
}
