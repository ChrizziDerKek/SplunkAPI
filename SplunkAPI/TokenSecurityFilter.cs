using Microsoft.OpenApi;
using SplunkAPI.Endpoints;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace SplunkAPI;

public class TokenSecurityFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<ApiTokenRequiredMetadata>().Any())
            return;
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement { [new("Bearer", context.Document)] = [] });
    }
}