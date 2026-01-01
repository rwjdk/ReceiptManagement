namespace Logic.Models;

public record BankEntry(DateOnly Date, string Text, decimal Amount, decimal Balance);