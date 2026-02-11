using System.Text;
using AgentFrameworkToolkit;
using AgentFrameworkToolkit.AzureOpenAI;
using AgentFrameworkToolkit.OpenAI;
using Logic.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Logic;

public class VectorStoreController(AzureOpenAIEmbeddingFactory embeddingFactory, AzureOpenAIAgentFactory agentFactory)
{
    public async Task SyncVectorStoreAsync(Action<string> notifyProgress, int year, string pathToUnprocessedPdfs)
    {
        SqliteCollection<string, VectorStoreRecord> vectorStoreCollection = await GetCollectionAsync(year);
        List<VectorStoreRecord> existingRecords = [];
        await foreach (VectorStoreRecord storeRecord in vectorStoreCollection.GetAsync(filter: record => record.Id != "", top: int.MaxValue))
        {
            existingRecords.Add(storeRecord);
        }

        string[] existingIds = existingRecords.Select(x => x.Id).ToArray();
        string[] unprocessedPdfs = Directory.GetFiles(pathToUnprocessedPdfs, "*.pdf", SearchOption.AllDirectories);

        List<string> activeIds = [];
        List<string> toIngest = [];
        foreach (string pdfPath in unprocessedPdfs)
        {
            activeIds.Add(pdfPath);
            if (existingIds.Contains(pdfPath))
            {
                continue;
            }

            toIngest.Add(pdfPath);
        }

        AzureOpenAIAgent invoiceDetailsAgent = agentFactory.CreateAgent(new AgentOptions
        {
            Model = OpenAIChatModels.Gpt5Mini,
            Instructions = "You are an Expert in analyzing raw PDF Data and extracting what was bought",
            ReasoningEffort = OpenAIReasoningEffort.Minimal
        });

        int counter = 0;
        foreach (string pdfPath in toIngest)
        {
            counter++;
            byte[] bytes = await File.ReadAllBytesAsync(pdfPath);
            PdfDocument document = PdfDocument.Open(bytes);
            string pdfText = string.Empty;
            foreach (Page page in document.GetPages())
            {
                IEnumerable<IPdfImage> images = page.GetImages();
                foreach (IPdfImage image in images)
                {
                    Console.WriteLine(""); //Todo - extract text from image
                }

                pdfText += page.Text + Environment.NewLine;
            }

            notifyProgress.Invoke($"- [{counter}/{toIngest.Count}] Ingesting unprocessed PDF to vector-store '{Path.GetFileName(pdfPath)}'");
            if (string.IsNullOrWhiteSpace(pdfText))
            {
                continue;
            }

            try
            {
                string instructions = $"""
                                       Clean up the following PDF text '{pdfText}' 
                                       so 
                                       - Company that issued of the Invoice
                                       - Product(s)
                                       - Dates
                                       - Amounts and Currency
                                       are left. 

                                       Rules:
                                       - Company that issued of the Invoice is never 'RWJ Invest'.
                                       - Remove customer date (RWJ Invest / Rasmus Wulff Jensen)
                                       - Remove their Address (Chr. Winthers vej 83,st,tv 8230 Åbyhøj)
                                       """;
                ChatClientAgentResponse<PdfDetails> response = await invoiceDetailsAgent.RunAsync<PdfDetails>(instructions);

                PdfDetails result = response.Result;
                await vectorStoreCollection.UpsertAsync(new VectorStoreRecord
                {
                    Id = pdfPath,
                    Content = result.ToString(pdfText),
                    Amount = Convert.ToInt32(result.Amount * 100),
                    Date = result.Date?.ToString("yyyyMMdd"),
                    Month = result.Month,
                    Issuer = result.Issuer,
                    FileName = Path.GetFileName(pdfPath)
                });
            }
            catch (Exception e)
            {
                await vectorStoreCollection.UpsertAsync(new VectorStoreRecord
                {
                    Id = pdfPath,
                    Content = pdfText,
                    FileName = Path.GetFileName(pdfPath)
                });
            }
        }

        IEnumerable<string> deadIds = existingIds.Except(activeIds);
        await vectorStoreCollection.DeleteAsync(deadIds);
    }

    public async Task<List<VectorStoreRecord>> Search(int year, string query)
    {
        SqliteCollection<string, VectorStoreRecord> vectorStoreCollection = await GetCollectionAsync(year);
        List<VectorStoreRecord> vectorStoreSearchResult = [];
        await foreach (VectorSearchResult<VectorStoreRecord> result in vectorStoreCollection.SearchAsync(query, 12, new VectorSearchOptions<VectorStoreRecord>
                       {
                           IncludeVectors = false
                       }))
        {
            vectorStoreSearchResult.Add(result.Record);
        }

        return vectorStoreSearchResult;
    }

    private async Task<SqliteCollection<string, VectorStoreRecord>> GetCollectionAsync(int year)
    {
        SqliteVectorStore vectorStore = new($"Data Source={Path.GetTempPath()}\\vector-store-{year}.db", new SqliteVectorStoreOptions
        {
            EmbeddingGenerator = embeddingFactory.GetEmbeddingGenerator("text-embedding-3-small")
        });

        SqliteCollection<string, VectorStoreRecord> vectorStoreCollection = vectorStore.GetCollection<string, VectorStoreRecord>("Data");
        await vectorStoreCollection.EnsureCollectionExistsAsync();
        return vectorStoreCollection;
    }
}