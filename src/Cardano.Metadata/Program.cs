using FastEndpoints;
using FastEndpoints.Swagger;
using Cardano.Metadata.Models;
using Microsoft.EntityFrameworkCore;
using Cardano.Metadata.Modules.Handlers;
using System.Net.Http.Headers;
using System.Reflection;
using Cardano.Metadata.Services;
using Cardano.Metadata.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument();

builder.Services.AddDbContextFactory<MetadataDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<MetadataHandler>();
builder.Services.AddSingleton<MetadataDbService>();
builder.Services.AddSingleton<GithubService>();
builder.Services.AddHostedService<GithubWorker>();

builder.Services.AddHttpClient("GithubApi", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    var productName = builder.Configuration["Github:UserAgent:ProductName"] ?? "CardanoTokenMetadataService";
    var productVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown Version";
    var productUrl = builder.Configuration["Github:UserAgent:ProductUrl"] ?? "(+https://github.com/SAIB-Inc/Cardano.Metadata)";
    
    ProductInfoHeaderValue productValue = new(productName, productVersion);
    ProductInfoHeaderValue commentValue = new(productUrl);
    client.DefaultRequestHeaders.UserAgent.Add(productValue);
    client.DefaultRequestHeaders.UserAgent.Add(commentValue);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", builder.Configuration["GithubPAT"]);
});

builder.Services.AddHttpClient("GithubRaw", client =>
{
    client.BaseAddress = new Uri("https://raw.githubusercontent.com/");
});

var app = builder.Build();

app.UseHttpsRedirection();

app.UseFastEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerGen();
}

app.Run();
