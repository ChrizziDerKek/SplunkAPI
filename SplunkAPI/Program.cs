using Microsoft.OpenApi;
using ModelContextProtocol.AspNetCore;
using SplunkAPI.Endpoints;
namespace SplunkAPI;

public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        Options options = Options.FromEnvironment();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<QueryGuard>();
        builder.Services.AddHttpClient<IManagementClient, ManagementClient>((svc, client) =>
        {
            Options opt = svc.GetRequiredService<Options>();
            client.BaseAddress = new(EnsureTrailingSlash(opt.ManagementUrl));
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(CreateHandler);
        builder.Services.AddHttpClient<IHecClient, HecClient>(client => client.Timeout = TimeSpan.FromSeconds(30)).ConfigurePrimaryHttpMessageHandler(CreateHandler);
        builder.Services.AddMcpServer().WithHttpTransport(opt => opt.SessionMode = HttpServerSessionMode.Stateless).WithTools<McpTools>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(opt =>
        {
            opt.SwaggerDoc("v2", new()
            {
                Title = "SplunkAPI",
                Version = "v2",
                Description = "REST and MCP interface for Splunk"
            });
            opt.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
            {
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "API Token",
                Description = "Enter the SPLUNK_API_TOKEN."
            });
            opt.OperationFilter<TokenSecurityFilter>();
        });
        WebApplication app = builder.Build();
        app.UseSwagger();
        app.UseSwaggerUI(opt => opt.SwaggerEndpoint("/swagger/v2/swagger.json", "SplunkAPI v2"));
        if (options.UsesLegacySearchApiToken)
            app.Logger.LogWarning("SPLUNK_SEARCH_API_TOKEN is being used as a backwards-compatible fallback. Prefer SPLUNK_API_TOKEN.");
        app.UseMiddleware<TokenMiddleware>();
        app.MapIngestEndpoints();
        app.MapSearchEndpoints();
        app.MapMcp("/mcp");
        app.Run();
    }

    private static HttpClientHandler CreateHandler(IServiceProvider svc)
    {
        Options opt = svc.GetRequiredService<Options>();
        HttpClientHandler handler = new();
        if (opt.ManagementIgnoreCertificateErrors)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        return handler;
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}