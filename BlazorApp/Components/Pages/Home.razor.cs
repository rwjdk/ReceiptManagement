using Blazored.LocalStorage;
using JetBrains.Annotations;
using Logic;
using Logic.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http.HttpResults;
using MudBlazor;
using System.Security.Principal;
using System.Text;

namespace BlazorApp.Components.Pages;

[UsedImplicitly]
public partial class Home(
    IConfiguration configuration,
    AccountController accountController,
    ISnackbar snackBar,
    YearController yearController,
    IDialogService dialogService,
    ILocalStorageService localStorageService)
{
    private string _rootFolder = null!;
    private readonly List<YearFolder> _years = [];
    private YearFolder? _selectedYear;
    private Account? _selectedAccount;
    private AccountRecord? _selectedRecord;
    private string[] _categories = [];
    private bool _loading;
    private MarkupString _loadingStatus;
    private bool _showConfirmed = false;
    private string[] _unprocessedPdfs = [];
    private MatchRule[] _matchRules = [];
    private bool _testMode;
    private bool _initialized;


    public IEnumerable<AccountRecord> LinesToShow
    {
        get
        {
            if (_selectedAccount == null)
            {
                return [];
            }

            return _showConfirmed ? _selectedAccount.Records.OrderBy(x=> x.BankEntry.Date).ThenBy(x => x.LineNum) : _selectedAccount.Records.Where(x => !x.Confirmed).OrderBy(x => x.BankEntry.Date).ThenBy(x=> x.LineNum);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _testMode = await localStorageService.GetItemAsync<bool?>("TestMode") ?? false;
        SwitchMode();
    }

    private void SwitchMode()
    {
        _initialized = false;
        _rootFolder = _testMode ? configuration["RootFolderTest"]! : configuration["RootFolderProd"]!;
        _categories = configuration.GetSection("Categories").Get<string[]>() ?? [];
        _matchRules = configuration.GetSection("MatchRules").Get<MatchRule[]>() ?? [];
        InitializeYears();
        _initialized = true;
    }

    private void InitializeYears()
    {
        AccountConfig[] accounts = configuration.GetSection("Accounts").Get<AccountConfig[]>() ?? [];
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

                Data data = yearController.GetOrCreate(dataFilePath, accounts);
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
            BankEntry[] entries = accountController.ReadBankEntries(bankFileContent).Where(x => x.Date.Year == _selectedYear.Year).ToArray();

            if (entries.Any())
            {
                string accountNumber = entries.First().AccountNumber;
                _selectedAccount = _selectedYear.Data.Accounts.FirstOrDefault(x => x.Number == accountNumber);
                List<AccountRecord> newRecords = await accountController.GetNewRecords(_selectedYear.Year, NotifyProgress, _matchRules, entries, _selectedYear.UnprocessedReceiptsFolder, _selectedAccount);
                snackBar.Add($"{newRecords.Count} new records imported", Severity.Success);
                _selectedAccount.Records.AddRange(newRecords);
                yearController.Update(_selectedYear);
            }
            else
            {
                snackBar.Add("No data in file", Severity.Warning);
            }
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
                string newAttachmentName = $"V{record.BankEntry.Date.ToString("yyyyMMdd")}-{record.Company} - {record.Description}{Path.GetExtension(record.Attachment)}";
                foreach (char invalidFileNameChar in Path.GetInvalidFileNameChars())
                {
                    newAttachmentName = newAttachmentName.Replace(invalidFileNameChar, '_');
                }

                string source = Path.Combine(_selectedYear.UnprocessedReceiptsFolder, record.Attachment);
                string target = Path.Combine(_selectedYear.ProcessedReceiptsFolder, newAttachmentName);

                if (File.Exists(source))
                {
                    File.Move(source, target);
                    record.Attachment = newAttachmentName;
                }
                else
                {
                    snackBar.Add("Source File do not exist. Removing attachment link");
                    record.Attachment = null;
                }
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

                if (File.Exists(source))
                {
                    File.Move(source, target);
                }
                else
                {
                    snackBar.Add("Source File do not exist. Removing attachment link");
                    record.Attachment = null;
                }
            }
        }

        yearController.Update(_selectedYear);
        if (!_showConfirmed)
        {
            _selectedRecord = LinesToShow.FirstOrDefault(x => !x.Confirmed);
        }
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

        _unprocessedPdfs = Directory.GetFiles(_selectedYear.UnprocessedReceiptsFolder, "*.pdf");
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

        try
        {
            _loading = true;
            await accountController.ReprocessRecord(NotifyProgress, accountRecord, _matchRules, _selectedYear);
            yearController.Update(_selectedYear);
        }
        finally
        {
            _loadingStatus = new MarkupString();
            _loading = false;
        }
    }

    private void ChangeMode(bool testMode)
    {
        _selectedYear = null;
        _selectedAccount = null;
        _selectedRecord = null;
        _years.Clear();
        _testMode = testMode;
        localStorageService.SetItemAsync("TestMode", testMode);
        SwitchMode();
        StateHasChanged();
    }

    private async Task DeleteEntriesForMonth(int monthNumber)
    {
        if (_selectedAccount == null || _selectedYear == null)
        {
            return;
        }

        List<AccountRecord> recordsToDelete = _selectedAccount.Records
            .Where(x => x.BankEntry.Date.Year == _selectedYear.Year && x.BankEntry.Date.Month == monthNumber)
            .ToList();

        if (recordsToDelete.Count == 0)
        {
            snackBar.Add("No records found for selected month", Severity.Info);
            return;
        }

        string monthName = new DateTime(_selectedYear.Year, monthNumber, 1).ToString("MMMM");
        bool? confirmed = await dialogService.ShowMessageBox(
            title: "Delete records for month",
            markupMessage: new MarkupString($"Are you sure you want to delete <strong>{recordsToDelete.Count}</strong> record(s) for <strong>{monthName} {_selectedYear.Year}</strong>?"),
            yesText: "Delete all",
            cancelText: "Cancel");

        if (confirmed != true)
        {
            return;
        }

        _selectedAccount.Records.RemoveAll(x => x.BankEntry.Date.Year == _selectedYear.Year && x.BankEntry.Date.Month == monthNumber);
        if (_selectedRecord != null && _selectedRecord.BankEntry.Date.Year == _selectedYear.Year && _selectedRecord.BankEntry.Date.Month == monthNumber)
        {
            _selectedRecord = null;
        }

        yearController.Update(_selectedYear);
        snackBar.Add($"Deleted {recordsToDelete.Count} records for {monthName} {_selectedYear.Year}", Severity.Success);
    }
}
