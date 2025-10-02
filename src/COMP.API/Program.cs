using FastEndpoints;
using FastEndpoints.Swagger;
using Scalar.AspNetCore;
using Microsoft.EntityFrameworkCore;
using COMP.Data.Data;
using COMP.API.Modules.Handlers;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextFactory<MetadataDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<MetadataHandler>();

builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument(o =>
{
    o.DocumentSettings = s =>
    {
        s.Title = "COMP API";
        s.Version = "v1";
    };
});

builder.Services.AddOpenApi();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseFastEndpoints();

app.UseSwaggerGen();

app.MapScalarApiReference(options =>
    options
        .WithTitle("COMP API")
        .WithTheme(ScalarTheme.Purple)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json")
);

app.Run();