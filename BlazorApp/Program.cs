using AgentFrameworkToolkit.AzureOpenAI;
using BlazorApp;
using Logic;
using Microsoft.AspNetCore.Mvc;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMudServices();

AzureOpenAIConnection connection = new()
{
    Endpoint = builder.Configuration["AzureOpenAIEndpoint"]!,
    ApiKey = builder.Configuration["AzureOpenAIApiKey"]!
};
builder.Services.AddMemoryCache();
builder.Services.AddAzureOpenAIAgentFactory(connection);
builder.Services.AddAzureOpenAIEmbeddingFactory(connection);
builder.Services.AddScoped<AccountController>();
builder.Services.AddScoped<YearController>();
builder.Services.AddScoped<VectorStoreController>();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.MapGet("/pdf", ([FromQuery] string filePath, [FromQuery] string unprocessedFolder, [FromQuery] string processedFolder) =>
{
    string path = Path.Combine(unprocessedFolder, filePath);
    if (File.Exists(path))
    {
        return Results.File(path, "application/pdf");
    }

    path = Path.Combine(processedFolder, filePath);
    if (File.Exists(path))
    {
        return Results.File(path, "application/pdf");
    }

    return Results.NotFound();
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<BlazorApp.Components.App>()
    .AddInteractiveServerRenderMode();
app.Run();