using Microsoft.AspNetCore.Authentication;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;
using NAVDataApi.Data;
using NAVDataApi.Models;
using NAVDataApi.Security;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(NavRecordRequest))
        {
            schema.Example = JsonNode.Parse("""
            {
              "fundCode": "TEST",
              "fundName": "Test Investment Fund",
              "shareClass": "A Acc",
              "isin": "ZZ9999999981",
              "currency": "USD",
              "navDate": "2026-10-21",
              "navPerShare": 125.4321,
              "sharesOutstanding": 1000000
            }
            """);
        }
        return Task.CompletedTask;
    });

    options.AddDocumentTransformer((document, _, _) =>
    {
        // Advertise the API key header so the interactive docs prompt for it.
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthOptions.HeaderName,
            Description = "Each API key has its own isolated dataset."
        };
        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationHandler.SchemeName, document)] = [] }
        ];
        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddControllers();

// API key authentication: the key identifies the client and therefore its dataset.
builder.Services.AddOptions<ApiKeyAuthOptions>()
    .Bind(builder.Configuration.GetSection(ApiKeyAuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

// Non-persistent data: one seeded in-memory dataset per client, restored on reset or restart.
// Datasets are capped and removed after their lifetime; the next request creates a fresh seed.
builder.Services.AddOptions<SandboxOptions>()
    .Bind(builder.Configuration.GetSection(SandboxOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<NavSandboxStore>();
builder.Services.AddExceptionHandler<SandboxCapacityExceptionHandler>();
builder.Services.AddHostedService<SandboxCleanupService>();
builder.Services.AddScoped<INavRepository>(sp =>
{
    var clientName = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User.Identity?.Name
        ?? throw new InvalidOperationException("A NAV dataset was requested without an authenticated client.");
    return sp.GetRequiredService<NavSandboxStore>().GetFor(clientName);
});
builder.Services.AddOptions<DataResetOptions>()
    .Bind(builder.Configuration.GetSection(DataResetOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<DataResetService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // Interactive API docs at /scalar

    // Swagger UI at /swagger, using the OpenAPI document generated above.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "NAVDataApi v1");
        options.EnablePersistAuthorization(); // Keep the API key across page refreshes.
    });
}

app.UseHttpsRedirection();

// Datasets vary by API key and must not be retained by browsers or intermediaries.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Vary = ApiKeyAuthOptions.HeaderName;
            return Task.CompletedTask;
        });
    }
    await next();
});

// The dashboard is public; every data request still requires an API key.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
