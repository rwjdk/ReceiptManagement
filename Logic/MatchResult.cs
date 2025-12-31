namespace Logic;

public record MatchResult(string Notes, string? Company, string? Description, string? Category, bool NeedAttachment, bool NeedDividedCompanyMatch = false);