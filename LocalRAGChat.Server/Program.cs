using CsvHelper;
using LocalRAGChat.Server.Services;
using LocalRAGChat.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.Memory;
using OllamaSharp;
using System.Globalization;

// This pragma disables experimental warnings for the entire memory stack.
#pragma warning disable SKEXP0001, SKEXP0021, SKEXP0020

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var connectionString = configuration.GetConnectionString("DefaultConnection")!;
var ollamaEndpoint = new Uri(configuration["Ollama:Endpoint"]!);
var embeddingModel = configuration["Ollama:EmbeddingModel"]!;
var ollama = new OllamaApiClient(ollamaEndpoint);
var store = await Microsoft.SemanticKernel.Connectors.Sqlite.SqliteMemoryStore.ConnectAsync(connectionString);
var embeddingGenerator = new OllamaSharpEmbeddingGeneration(ollama, embeddingModel);
var memory = new SemanticTextMemory(store, embeddingGenerator);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContextFactory<RagDbContext>(options =>
    options.UseSqlite(connectionString));

var corsSettings = configuration.GetSection("Cors");
var allowedOrigins = corsSettings["AllowedOrigins"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorWasm",
        policy =>
        {
            policy.SetIsOriginAllowed(origin => new Uri(origin).Host == "localhost")
                 .AllowAnyHeader()
                 .AllowAnyMethod();
        });
});

builder.Services.AddSingleton(ollama);
builder.Services.AddSingleton<ISemanticTextMemory>(memory);

builder.Services.AddScoped<RagService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowBlazorWasm");
app.UseAuthorization();
app.MapControllers();

app.Run();

