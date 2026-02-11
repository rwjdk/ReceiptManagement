using AgentFrameworkToolkit.AzureOpenAI;
using AgentFrameworkToolkit.OpenAI;
using JetBrains.Annotations;
using Logic.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using AgentFrameworkToolkit;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Logic;

//todo- refactor and clean
public class AccountController(AzureOpenAIAgentFactory agentFactory, VectorStoreController vectorStoreController)
{
    private const string MultipleMatchesDescription = "MULTIPLE MATCHES - FIX THIS";

    public async Task ReprocessRecord(Action<string> notifyProgress, AccountRecord accountRecord, MatchRule[] matchRules, YearFolder yearFolder)
    {
        await Task.CompletedTask;
        AccountRecord record = FromBankEntries([accountRecord.BankEntry], matchRules)[0];
        if (!string.IsNullOrWhiteSpace(record.Company))
        {
            accountRecord.Company = record.Company;
        }

        if (!string.IsNullOrWhiteSpace(record.Description))
        {
            accountRecord.Description = record.Description;
        }

        if (!string.IsNullOrWhiteSpace(record.Category))
        {
            accountRecord.Category = record.Category;
        }
        accountRecord.MatchResults = record.MatchResults;
        await vectorStoreController.SyncVectorStoreAsync(notifyProgress, yearFolder.Year, yearFolder.UnprocessedReceiptsFolder);
        if (accountRecord.MatchResults.Length == 1)
        {
            MatchResult matchResult = accountRecord.MatchResults[0];
            await ProcessDividedCompanyMatch(matchResult, accountRecord, GetDividendCompanyAgent());
        }
        await ProcessAttachmentMatch(yearFolder.Year, notifyProgress, null, accountRecord, GetInvoiceDetailsAgent());
    }

    public async Task<List<AccountRecord>> GetNewRecords(
        int year,
        Action<string> notifyProgress,
        MatchRule[] matchRules,
        BankEntry[] entries,
        string pathToUnprocessedPdfs,
        Account account)
    {
        List<AccountRecord> newRecords = [];
        AccountRecord[] fromBankEntries = FromBankEntries(entries, matchRules.ToArray());
        List<AccountRecord> toProcess = [];
        foreach (AccountRecord newRecord in fromBankEntries.Reverse())
        {
            if (account.Records.Any(x => x.BankEntry.MatchKey() == newRecord.BankEntry.MatchKey()))
            {
                continue;
            }

            toProcess.Add(newRecord);
        }

        if (toProcess.Count == 0)
        {
            return [];
        }

        notifyProgress.Invoke($"Found {toProcess.Count} new records to process");

        await vectorStoreController.SyncVectorStoreAsync(notifyProgress, year, pathToUnprocessedPdfs);

        int nextLineNum = account.Records.MaxBy(x => x.LineNum)?.LineNum ?? 0;

        AzureOpenAIAgent dividendCompanyAgent = GetDividendCompanyAgent();
        AzureOpenAIAgent invoiceDetailsAgent = GetInvoiceDetailsAgent();

        int counter = 1;
        foreach (AccountRecord newRecord in toProcess)
        {
            notifyProgress.Invoke($"- [{counter}/{toProcess.Count}] Processing new record 'Date: {newRecord.BankEntry.Date.ToString("dd. MMM")} Amount: {newRecord.BankEntry.Amount:N2} - Text: {newRecord.BankEntry.Text}'");
            counter++;
            if (newRecord.MatchResults.Length == 1)
            {
                //Good match
                MatchResult matchResult = newRecord.MatchResults[0];

                await ProcessDividedCompanyMatch(matchResult, newRecord, dividendCompanyAgent);
                await ProcessAttachmentMatch(year, notifyProgress, matchResult, newRecord, invoiceDetailsAgent);
            }

            newRecord.LineNum = nextLineNum;
            newRecords.Add(newRecord);
            nextLineNum++;
        }

        return newRecords;
    }

    private AzureOpenAIAgent GetInvoiceDetailsAgent()
    {
        return agentFactory.CreateAgent(new AgentOptions
        {
            Model = OpenAIChatModels.Gpt5Mini,
            Instructions = "You are an Expert in analyzing Invoices and what was purchased",
            ReasoningEffort = OpenAIReasoningEffort.Minimal
        });
    }

    private async Task ProcessAttachmentMatch(int year, Action<string> notifyProgress, MatchResult? matchResult, AccountRecord newRecord, AzureOpenAIAgent invoiceDetailsAgent)
    {
        if (matchResult == null || matchResult.NeedAttachment)
        {
            string query = newRecord.ToString();
            List<VectorStoreRecord> vectorStoreSearchResult = await vectorStoreController.Search(year, query);
            StringBuilder searchResult = new();
            foreach (VectorStoreRecord record in vectorStoreSearchResult)
            {
                searchResult.AppendLine(record.ToString());
            }

            string whatFileOfThese = "What File of these: " + searchResult + $" is the best match for this record: {newRecord} (Issuer, Amount (Might be different currency so adjust) and Month/Approximate Date is the best match-conditions). If nothing match then leave Filename null";
            ChatClientAgentResponse<DocumentMatch> responseDocumentMatch = await invoiceDetailsAgent.RunAsync<DocumentMatch>(whatFileOfThese);
            VectorStoreRecord? bestMatch = vectorStoreSearchResult.FirstOrDefault(x => x.FileName.Equals(responseDocumentMatch.Result.FileName, StringComparison.CurrentCultureIgnoreCase));

            newRecord.Attachment = bestMatch?.FileName;

            if (string.IsNullOrWhiteSpace(newRecord.Description) && bestMatch != null)
            {
                notifyProgress.Invoke($"-- Determine what was purchased from {newRecord.Company})");
                ChatClientAgentResponse<InvoiceResult> response = await invoiceDetailsAgent.RunAsync<InvoiceResult>("What was purchased here: " + bestMatch.Content);
                newRecord.Description = response.Result.ProductPurchased;
                if (string.IsNullOrWhiteSpace(newRecord.Category))
                {
                    newRecord.Category = response.Result.Category;
                }
            }
        }
    }

    private AzureOpenAIAgent GetDividendCompanyAgent()
    {
        AzureOpenAIAgent dividendCompanyAgent = agentFactory.CreateAgent(new AgentOptions
        {
            Model = OpenAIChatModels.Gpt52,
            Instructions = "You are a Stock Expert",
            ReasoningEffort = OpenAIReasoningEffort.Minimal
        });
        return dividendCompanyAgent;
    }

    private static async Task ProcessDividedCompanyMatch(MatchResult matchResult, AccountRecord newRecord, AzureOpenAIAgent dividendCompanyAgent)
    {
        if (matchResult.NeedDividedCompanyMatch)
        {
            string description = newRecord.Description ?? string.Empty;
            ChatClientAgentResponse<DividendCompanyResult> response = await dividendCompanyAgent.RunAsync<DividendCompanyResult>("What company does this refer to?: " + newRecord.BankEntry.Text);
            string yieldCompany = response.Result.CompanyName;
            description = description.Replace("<COMPANY>", yieldCompany);
            newRecord.Description = description;
        }
    }

    public BankEntry[] ReadBankEntries(string content)
    {
        List<BankEntry> result = [];
        string[] lines = content.Split('\n');
        foreach (string line in lines.Skip(1))
        {
            string[] parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries);
            DateOnly date = DateOnly.ParseExact(parts[0], "yyyy/MM/dd");
            decimal amount = decimal.Parse(parts[1], new NumberFormatInfo
            {
                NumberDecimalSeparator = ","
            });
            string text = parts[3];
            string accountNumber = parts[2];
            decimal balance = decimal.Parse(parts[4], new NumberFormatInfo
            {
                NumberDecimalSeparator = ","
            });
            result.Add(new BankEntry(date, text, amount, balance, accountNumber));
        }

        return result.ToArray();
    }

    public AccountRecord[] FromBankEntries(BankEntry[] bankEntries, MatchRule[] matchRules)
    {
        List<AccountRecord> result = [];
        foreach (BankEntry bankEntry in bankEntries)
        {
            MatchResult[] matchResults = matchRules.Where(x => x.Match(bankEntry) != null).Select(x => x.Match(bankEntry)!).ToArray();
            switch (matchResults.Length)
            {
                case 1:
                    MatchResult match = matchResults[0];
                    result.Add(new AccountRecord(bankEntry, matchResults, match.Company, match.Description, match.Category, false));
                    break;
                case 0:
                    result.Add(new AccountRecord(bankEntry, matchResults, null, null, null, false));
                    break;
                default:
                    result.Add(new AccountRecord(bankEntry, matchResults, null, MultipleMatchesDescription, null, false));
                    break;
            }
        }

        return result.ToArray();
    }

    [UsedImplicitly]
    private class DividendCompanyResult
    {
        [Description("Just the company name, nothing else")]
        public required string CompanyName { get; set; }
    }

    [UsedImplicitly]
    private class DocumentMatch
    {
        public required string? FileName { get; set; }
    }

    [UsedImplicitly]
    private class InvoiceResult
    {
        [Description("Just the product-name/type of product")]
        public required string ProductPurchased { get; set; }

        [Description("Choose among the following categories ['It Udstyr','Kontorudstyr', or 'Services']")] //todo: make this part of instructions instead and from a configurable source
        public required string Category { get; set; }
    }
}
