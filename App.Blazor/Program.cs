using AgentFrameworkToolkit.AzureOpenAI;
using Blazor;
using Logic;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Connectors.InMemory;
using MudBlazor.Services;
using SimpleRag;
using SimpleRag.VectorStorage.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMudServices();

VectorStoreConfiguration vectorStoreConfiguration = new("UnprocessedAttachments");
AzureOpenAIConnection connection = new()
{
    Endpoint = builder.Configuration["AzureOpenAIEndpoint"]!,
    ApiKey = builder.Configuration["AzureOpenAIApiKey"]!
};
builder.Services.AddMemoryCache();
builder.Services.AddAzureOpenAIAgentFactory(connection);
builder.Services.AddAzureOpenAIEmbeddingFactory(connection);
builder.Services.AddEmbeddingGenerator(provider =>
{
    AzureOpenAIEmbeddingFactory embeddingFactory = provider.GetRequiredService<AzureOpenAIEmbeddingFactory>();
    return embeddingFactory.GetEmbeddingGenerator("text-embedding-3-small");
});

builder.Services.AddSimpleRag(vectorStoreConfiguration, provider => new InMemoryVectorStore(new InMemoryVectorStoreOptions
{
    EmbeddingGenerator = provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()
}));

builder.Services.AddSingleton<BankFileQuery>();
builder.Services.AddSingleton<FinancialRecordQuery>();
builder.Services.AddSingleton<FinancialRecordCommand>();
builder.Services.AddScoped<Controller>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.MapGet("/pdf/{fileName}", (string fileName) =>
{
    string filePath = Path.Combine(Paths.PathToUnprocessedPdfs, fileName);

    if (File.Exists(filePath))
    {
        return Results.File(filePath, "application/pdf");
    }

    filePath = Path.Combine(Paths.PathToProcessedPdfs, fileName);
    if (File.Exists(filePath))
    {
        return Results.File(filePath, "application/pdf");
    }

    return Results.NotFound();
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<Blazor.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();