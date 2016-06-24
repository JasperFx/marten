using System;
using Marten;

namespace DinnerParty.Models.Marten
{
    /// <summary>
    /// A <seealso cref="MartenRegistry"/> to configure document storage for the <see cref="Dinner"/> model
    /// </summary>
    /// <remarks>
    /// This class demonstrates adding a GIN index to the jsonb column (the document column)
    /// see: https://github.com/JasperFx/marten/blob/master/documentation/documentation/documents/customizing.md#gin-indexes
    /// </remarks>
    public class DinnersRegistry : MartenRegistry
    {
        public DinnersRegistry()
        {
            // Generate a gin index on the Dinner JSONB data
            For<Dinner>().GinIndexJsonData();

            // Make the LastModified column searchable
            For<Dinner>().Duplicate(d => d.LastModified);
        }
    }
}