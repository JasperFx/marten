using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using IssueService.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

/// <summary>
/// <see cref="StreamPagedByCursor{T}"/> is documented as contributing OpenAPI metadata like the
/// other typed result wrappers, so it has to publish the envelope it actually streams rather than
/// <c>void</c> (https://github.com/JasperFx/marten/issues/5131).
/// </summary>
public class stream_paged_by_cursor_metadata_tests
{
    [Fact]
    public void publishes_the_streamed_envelope_as_the_200_response_type()
    {
        // Given
        var builder = CreateEndpointBuilder();

        // When
        StreamPagedByCursor<Issue>.PopulateMetadata(MethodInfoPlaceholder(), builder);

        // Then
        var metadata = builder.Metadata.OfType<IProducesResponseTypeMetadata>().Single();

        metadata.StatusCode.ShouldBe(StatusCodes.Status200OK);
        metadata.Type.ShouldBe(typeof(CursorPagedResult<Issue>));
        metadata.ContentTypes.ShouldBe(new[] { "application/json" });
    }

    [Fact]
    public void publishes_the_same_shape_as_the_offset_paged_wrapper()
    {
        // Given
        var cursorBuilder = CreateEndpointBuilder();
        var offsetBuilder = CreateEndpointBuilder();

        // When
        StreamPagedByCursor<Issue>.PopulateMetadata(MethodInfoPlaceholder(), cursorBuilder);
        StreamPaged<Issue>.PopulateMetadata(MethodInfoPlaceholder(), offsetBuilder);

        // Then
        var cursorMetadata = cursorBuilder.Metadata.OfType<IProducesResponseTypeMetadata>().Single();
        var offsetMetadata = offsetBuilder.Metadata.OfType<IProducesResponseTypeMetadata>().Single();

        cursorMetadata.StatusCode.ShouldBe(offsetMetadata.StatusCode);
        cursorMetadata.ContentTypes.ShouldBe(offsetMetadata.ContentTypes);
        cursorMetadata.Type.ShouldNotBe(typeof(void));
    }

    private static EndpointBuilder CreateEndpointBuilder()
    {
        return new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/issues"), 0);
    }

    private static MethodInfo MethodInfoPlaceholder()
    {
        // PopulateMetadata does not read the method; the parameter is part of the
        // IEndpointMetadataProvider contract.
        return typeof(stream_paged_by_cursor_metadata_tests).GetMethod(nameof(MethodInfoPlaceholder), BindingFlags.NonPublic | BindingFlags.Static)!;
    }
}
