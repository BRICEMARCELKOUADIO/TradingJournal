using System.ComponentModel.DataAnnotations;
using TradingJournal.Domain.Enums;

namespace TradingJournal.Application.Trades;

public sealed class TradeQuery : IValidatableObject
{
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    [EnumDataType(typeof(MarketType))]
    public MarketType? MarketType { get; set; }
    [StringLength(30)]
    public string? Symbol { get; set; }
    [EnumDataType(typeof(Status))]
    public Status? Status { get; set; }
    [EnumDataType(typeof(Direction))]
    public Direction? Direction { get; set; }
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;
    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate < StartDate) yield return new("La fin doit suivre le début.", new[] { nameof(EndDate) });
        if (((long)Page - 1) * PageSize > int.MaxValue)
            yield return new("Pagination trop élevée.", new[] { nameof(Page) });
    }
}
