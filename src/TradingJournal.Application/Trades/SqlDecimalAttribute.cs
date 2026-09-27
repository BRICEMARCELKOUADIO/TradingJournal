using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Application.Trades;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SqlDecimalAttribute(int precision, int scale) : ValidationAttribute
{
    public bool Positive { get; set; }

    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        if (value is not decimal number) return false;
        var limit = 1m;
        for (var i = 0; i < precision - scale; i++) limit *= 10m;
        return (!Positive || number > 0) && Math.Abs(number) < limit && decimal.Round(number, scale) == number;
    }

    public override string FormatErrorMessage(string name) =>
        $"{name} doit respecter decimal({precision},{scale}){(Positive ? " et être strictement positif" : "")}.";
}
