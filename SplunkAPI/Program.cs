using Microsoft.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ModelContextProtocol.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using SplunkAPI.Endpoints;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;
namespace SplunkAPI;

public partial class Program
{
    public static async Task Main(string[] args)
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
        builder.Services.Configure<ForwardedHeadersOptions>(opt => opt.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost);
        string oauthDbPath = builder.Configuration["SPLUNK_OAUTH_DB_PATH"] ?? Path.Combine(Path.GetTempPath(), "openiddict.db");
        string? oauthDbDirectory = Path.GetDirectoryName(oauthDbPath);
        if (!string.IsNullOrWhiteSpace(oauthDbDirectory))
            Directory.CreateDirectory(oauthDbDirectory);
        builder.Services.AddDbContext<OAuth>(opt =>
        {
            opt.UseSqlite($"Data Source={oauthDbPath}");
            opt.UseOpenIddict();
        });
        builder.Services.AddOpenIddict().AddCore(opt => opt.UseEntityFrameworkCore().UseDbContext<OAuth>()).AddServer(opt =>
        {
            opt.SetAuthorizationEndpointUris("/authorize");
            opt.SetTokenEndpointUris("/token");
            opt.AllowAuthorizationCodeFlow();
            opt.RequireProofKeyForCodeExchange();
            opt.RegisterScopes("splunk:read");
            opt.AddEphemeralEncryptionKey();
            opt.AddEphemeralSigningKey();
            opt.UseAspNetCore().EnableAuthorizationEndpointPassthrough();
        }).AddValidation(opt =>
        {
            opt.UseLocalServer();
            opt.UseAspNetCore();
        });
        builder.Services.AddAuthentication(opt =>
        {
            opt.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            opt.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });
        builder.Services.AddAuthorizationBuilder().AddPolicy("McpRead", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => context.User.HasScope("splunk:read"));
        });
        WebApplication app = builder.Build();
        if (options.UsesLegacySearchApiToken)
            app.Logger.LogWarning("SPLUNK_SEARCH_API_TOKEN is being used as a backwards-compatible fallback. Prefer SPLUNK_API_TOKEN.");
        app.UseForwardedHeaders();
        app.UseSwagger();
        app.UseSwaggerUI(opt => opt.SwaggerEndpoint("/swagger/v2/swagger.json", "SplunkAPI v2"));
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/mcp"))
            {
                context.Response.OnStarting(() =>
                {
                    if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                    {
                        string origin = $"{context.Request.Scheme}://" + $"{context.Request.Host}";
                        context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=" + $"\"{origin}/.well-known/" + $"oauth-protected-resource\", " + $"scope=\"splunk:read\"";
                    }
                    return Task.CompletedTask;
                });
            }
            await next();
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<TokenMiddleware>();
        app.MapIngestEndpoints();
        app.MapSearchEndpoints();
        app.MapMcp("/mcp").RequireAuthorization("McpRead");
        app.MapGet("/.well-known/oauth-protected-resource", (HttpRequest request) =>
        {
            string origin = $"{request.Scheme}://{request.Host}";
            string scope = "splunk:read";
            return Results.Json(new
            {
                resource = $"{origin}/mcp",
                authorization_servers = new[] { origin },
                scopes_supported = new[] { scope }
            });
        });
        app.MapMethods("/authorize", ["GET", "POST"], async (HttpContext context, IConfiguration config) =>
        {
            OpenIddictRequest request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("OpenIddict request is missing.");
            if (HttpMethods.IsGet(context.Request.Method))
            {
                string action = context.Request.Path + context.Request.QueryString;
                string html = $$"""
                <!doctype html>
                <html>
                <head>
                    <title>Splunk MCP Login</title>
                </head>
                <body>
                    <h2>Splunk MCP Login</h2>

                    <form method="post" action="{{action}}">
                        <label>
                            Username
                            <input name="username" autocomplete="username" />
                        </label>

                        <br />

                        <label>
                            Password
                            <input
                                type="password"
                                name="password"
                                autocomplete="current-password" />
                        </label>

                        <br />

                        <button type="submit">Authorize</button>
                    </form>
                </body>
                </html>
                """;
                return Results.Content(html, "text/html");
            }
            IFormCollection form = await context.Request.ReadFormAsync();
            string username = form["username"].ToString();
            string password = form["password"].ToString();
            string? expectedUser = config["SPLUNK_OAUTH_USERNAME"];
            string? expectedPassword = config["SPLUNK_OAUTH_PASSWORD"];
            if (!string.Equals(username, expectedUser, StringComparison.Ordinal) || !string.Equals(password, expectedPassword, StringComparison.Ordinal))
                return Results.Unauthorized();
            ClaimsIdentity identity = new(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, username);
            identity.SetClaim(Claims.Name, username);
            identity.SetScopes(request.GetScopes());
            string resource = $"{context.Request.Scheme}://" + $"{context.Request.Host}/mcp";
            string requestedResource = context.Request.Query["resource"].ToString();
            if (!string.IsNullOrWhiteSpace(requestedResource) && !string.Equals(requestedResource, resource, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "invalid_target" });
            ClaimsPrincipal principal = new(identity);
            principal.SetResources(resource);
            return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        });
        using (IServiceScope scope = app.Services.CreateScope())
        {
            OAuth db = scope.ServiceProvider.GetRequiredService<OAuth>();
            await db.Database.EnsureCreatedAsync();
            IOpenIddictApplicationManager manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            string clientId = builder.Configuration["SPLUNK_OAUTH_CLIENT_ID"] ?? throw new InvalidOperationException("SPLUNK_OAUTH_CLIENT_ID is missing.");
            string clientSecret = builder.Configuration["SPLUNK_OAUTH_CLIENT_SECRET"] ?? throw new InvalidOperationException("SPLUNK_OAUTH_CLIENT_SECRET is missing.");
            string redirectUri = builder.Configuration["SPLUNK_OAUTH_REDIRECT_URI"] ?? throw new InvalidOperationException("SPLUNK_OAUTH_REDIRECT_URI is missing.");
            if (await manager.FindByClientIdAsync(clientId) is null)
            {
                OpenIddictApplicationDescriptor descriptor = new()
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,

                    ClientType = ClientTypes.Confidential,
                    ConsentType = ConsentTypes.Implicit,
                    RedirectUris = { new(redirectUri) },
                    Permissions =
                    {
                        Permissions.Endpoints.Authorization,
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.AuthorizationCode,
                        Permissions.ResponseTypes.Code,
                        Permissions.Prefixes.Scope + "splunk:read"
                    },
                    Requirements = { Requirements.Features.ProofKeyForCodeExchange }
                };
                await manager.CreateAsync(descriptor);
            }
        }
        await app.RunAsync();
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