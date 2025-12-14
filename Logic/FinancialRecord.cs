namespace Logic;

public record FinancialRecord(bool Matched, DateOnly Date, string BankText, decimal Amount, string? Company, string? Description, string? Category, string? Link);