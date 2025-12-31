using AgentFrameworkToolkit.AzureOpenAI;
using BlazorApp;
using Logic;
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
builder.Services.AddSingleton<BankContentQuery>();
builder.Services.AddSingleton<AccountQuery>();
builder.Services.AddSingleton<AccountCommand>();
builder.Services.AddScoped<FinancialRecordQuery>();
builder.Services.AddScoped<ConfigurationQuery>();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

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
app.MapRazorComponents<BlazorApp.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();