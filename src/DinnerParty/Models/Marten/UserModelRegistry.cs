using System;
using Marten;

namespace DinnerParty.Models.Marten
{
    /// <summary>
    /// A <seealso cref="MartenRegistry"/> to configure document storage for the <see cref="UserModel"/> model
    /// </summary>
    /// <remarks>
    /// This class demonstrates adding searchable fields to the document's table
    /// see: https://github.com/JasperFx/marten/blob/master/documentation/documentation/documents/customizing.md#searchable-fields
    /// </remarks>
    public class UserModelRegistry : MartenRegistry
    {
        public UserModelRegistry()
        {
            // Generate a searchable index for UserModel.Username
            For<UserModel>().Duplicate(u => u.Username);
            
            // Generate a searchable index for UserModel.EMailAddress
            For<UserModel>().Duplicate(u => u.EMailAddress);
        }
    }
}