using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing
{
    public class document_session_storing_from_another_assembly_Tests : DocumentSessionFixture<IdentityMap>
    {
        [Fact]
        public void store_document_inherited_from_document_with_id_from_another_assembly()
        {
            var user = new UserFromBaseDocument();
            theSession.Store(user);
            theSession.Load<UserFromBaseDocument>(user.Id).ShouldBeTheSameAs(user);
        }
    }
}
