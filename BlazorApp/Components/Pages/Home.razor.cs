using JetBrains.Annotations;
using Logic;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using System.Text;
using Logic.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorApp.Components.Pages;

[UsedImplicitly]
public partial class Home(
    IConfiguration configuration,
    AccountController accountController,
    ISnackbar snackBar,
    YearController yearController)
{
    private string _rootFolder = null!;
    private readonly List<YearFolder> _years = [];
    private YearFolder? _selectedYear;
    private Account? _selectedAccount;
    private AccountRecord? _selectedRecord;
    private string[] _categories = [];
    private bool _loading;
    private MarkupString _loadingStatus;
    private bool _showConfirmed = true;
    private string[] _unprocessedPdfs = [];
    private MatchRule[] _matchRules = [];

    public IEnumerable<AccountRecord> LinesToShow
    {
        get
        {
            if (_selectedAccount == null)
            {
                return [];
            }

            return _showConfirmed ? _selectedAccount.Records.OrderByDescending(x => x.LineNum) : _selectedAccount.Records.Where(x => !x.Confirmed).OrderByDescending(x => x.LineNum);
        }
    }

    protected override void OnInitialized()
    {
        _rootFolder = configuration["RootFolder"]!;
        _categories = configuration.GetSection("Categories").Get<string[]>() ?? [];
        _matchRules = configuration.GetSection("MatchRules").Get<MatchRule[]>() ?? [];
        InitializeYears();
    }

    private void InitializeYears()
    {
        string[] accountNames = configuration.GetSection("AccountNames").Get<string[]>() ?? [];
        int currentYear = DateTime.Today.Year;
        string currentFolder = Path.Combine(_rootFolder, currentYear.ToString());
        if (!Directory.Exists(currentFolder))
        {
            Directory.CreateDirectory(currentFolder);
        }

        for (int year = 2000; year < currentYear + 1; year++)
        {
            string yearFolder = Path.Combine(_rootFolder, year.ToString());
            if (Directory.Exists(yearFolder))
            {
                string dataFilePath = Path.Combine(yearFolder, "Data.json");

                Data data = yearController.GetOrCreate(dataFilePath, accountNames);
                YearFolder folder = new(year, yearFolder, data);
                Directory.CreateDirectory(folder.UnprocessedReceiptsFolder);
                Directory.CreateDirectory(folder.ProcessedReceiptsFolder);
                if (folder.Year == currentYear)
                {
                    _selectedYear = folder;
                    _selectedAccount = _selectedYear.Data.Accounts.FirstOrDefault();
                }

                _years.Add(folder);
            }
        }
    }

    private void NotifyProgress(string obj)
    {
        _loadingStatus = new MarkupString($"{_loadingStatus.Value}<br/>{obj}");
        StateHasChanged();
    }

    private void SelectRow(DataGridRowClickEventArgs<AccountRecord> arg)
    {
        _selectedRecord = arg.Item;
    }

    private async Task UploadFile(IBrowserFile? file)
    {
        if (_selectedAccount == null || file == null || _selectedYear == null)
        {
            return;
        }

        try
        {
            _loading = true;
            await using Stream stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            using StreamReader reader = new(stream, Encoding.UTF8);
            string bankFileContent = await reader.ReadToEndAsync();

            List<AccountRecord> newRecords = await accountController.GetNewRecords(_selectedYear.Year, NotifyProgress, _matchRules, bankFileContent, _selectedYear.UnprocessedReceiptsFolder, _selectedAccount);
            snackBar.Add($"{newRecords.Count} new records imported", Severity.Success);
            _selectedAccount.Records.AddRange(newRecords);
            yearController.Update(_selectedYear);
        }
        finally
        {
            _loadingStatus = new MarkupString();
            _loading = false;
        }
    }

    private void UpdateRecord(AccountRecord record)
    {
        if (_selectedAccount == null || _selectedYear == null)
        {
            return;
        }

        if (!record.IsComplete)
        {
            record.Confirmed = false;
        }

        yearController.Update(_selectedYear);
    }

    private void Confirm(AccountRecord record)
    {
        if (_selectedAccount == null || _selectedYear == null)
        {
            return;
        }

        record.Confirmed = !record.Confirmed;

        if (record.Confirmed)
        {
            //Record is now confirmed. 
            if (!string.IsNullOrWhiteSpace(record.Attachment))
            {
                //Rename attachment and move to confirmed Attachments
                string newAttachmentName = record.Company + " - " + record.Description + Path.GetExtension(record.Attachment);
                string source = Path.Combine(_selectedYear.UnprocessedReceiptsFolder, record.Attachment);
                string target = Path.Combine(_selectedYear.ProcessedReceiptsFolder, newAttachmentName);
                File.Move(source, target);
                record.Attachment = newAttachmentName;
            }
        }
        else
        {
            //Record is now un-confirmed. 
            if (!string.IsNullOrWhiteSpace(record.Attachment))
            {
                //Move to confirmed Attachment back to un-confirmed (but leave name)
                string source = Path.Combine(_selectedYear.ProcessedReceiptsFolder, record.Attachment);
                string target = Path.Combine(_selectedYear.UnprocessedReceiptsFolder, record.Attachment);
                File.Move(source, target);
            }
        }

        yearController.Update(_selectedYear);
    }

    private void ChooseAttachment(string file, AccountRecord record)
    {
        if (_selectedAccount == null || _selectedYear == null)
        {
            return;
        }

        record.Attachment = Path.GetFileName(file);
        yearController.Update(_selectedYear);
    }

    private void RemoveAttachment(AccountRecord record)
    {
        if (_selectedAccount == null || _selectedYear == null)
        {
            return;
        }

        record.Attachment = null;
        yearController.Update(_selectedYear);
    }

    private void GetUnprocessedPdfs()
    {
        if (_selectedYear == null)
        {
            return;
        }

        _unprocessedPdfs = Directory.GetFiles(_selectedYear.UnprocessedReceiptsFolder);
    }

    private string RowClassFunc(AccountRecord record, int line)
    {
        return record == _selectedRecord ? "selectedRow" : "";
    }

    private void ChooseYear(YearFolder yearFolder)
    {
        _selectedYear = yearFolder;
        _selectedAccount = _selectedYear.Data.Accounts.FirstOrDefault();
        StateHasChanged();
    }

    private async Task ReprocessRecord(AccountRecord accountRecord)
    {
        if (_selectedYear == null)
        {
            return;
        }

        //todo: Confirm?
        await accountController.ReprocessRecord(accountRecord, _matchRules);
        yearController.Update(_selectedYear);
    }
}