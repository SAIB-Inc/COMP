using System.Net.Http.Headers;
using System.Reflection;
using Cardano.Metadata.Data;
using Cardano.Metadata.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContextFactory<TokenMetadataDbContext>(options =>
{
    options.EnableSensitiveDataLogging(true);
    options.UseNpgsql(builder.Configuration.GetConnectionString("TokenMetadataService"));
});

builder.Services.AddHostedService<TokenMetadataWorker>();

builder.Services.AddHttpClient("Github", client =>
{
    ProductInfoHeaderValue productValue = new("CardanoTokenMetadataService", Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown Version");
    ProductInfoHeaderValue commentValue = new("(+https://github.com/SAIB-Inc/Cardano.Metadata)");
    client.DefaultRequestHeaders.UserAgent.Add(productValue);
    client.DefaultRequestHeaders.UserAgent.Add(commentValue);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", builder.Configuration["GithubPAT"]);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
