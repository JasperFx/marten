#nullable enable
using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace EventSourcingTests.SchemaChange
{
    namespace Default
    {
        #region sample_schema_migration_default_event

        public record ShoppingCartOpened(
            Guid ShoppingCartId,
            Guid ClientId
        );

        #endregion
    }

    namespace WithNotRequiredProperty
    {
        #region sample_schema_migration_not_required_property

        public record ShoppingCartOpened(
            Guid ShoppingCartId,
            Guid ClientId,
            // Adding new not required property as nullable
            DateTime? OpenedAt
        );

        #endregion
    }

    namespace WithRequiredProperty
    {
        #region sample_schema_migration_required_property

        public enum ShoppingCartStatus
        {
            UnderFraudDetection = 1,
            Opened = 2,
            Confirmed = 3,
            Cancelled = 4
        }

        public record ShoppingCartOpened(
            Guid ShoppingCartId,
            Guid ClientId,
            // Adding new required property with default value
            ShoppingCartStatus Status = ShoppingCartStatus.Opened
        );

        #endregion
    }

    namespace WithRenamedProperty
    {
        namespace SystemTextJson
        {
            #region sample_schema_migration_renamed_property_stj

            public class ShoppingCartOpened
            {
                [JsonPropertyName("ShoppingCartId")]
                public Guid CartId { get; }
                public Guid ClientId { get; }

                public ShoppingCartOpened(
                    Guid cartId,
                    Guid clientId
                )
                {
                    CartId = cartId;
                    ClientId = clientId;
                }
            }

            #endregion
        }

        namespace JsonNet
        {
            #region sample_schema_migration_renamed_property_jsonnet

            public class ShoppingCartOpened
            {
                [JsonProperty("ShoppingCartId")]
                public Guid CartId { get; }
                public Guid ClientId { get; }

                public ShoppingCartOpened(
                    Guid cartId,
                    Guid clientId
                )
                {
                    CartId = cartId;
                    ClientId = clientId;
                }
            }

            #endregion
        }
    }
}
