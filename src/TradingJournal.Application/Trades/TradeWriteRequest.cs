using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TradingJournal.Domain.Enums;

namespace TradingJournal.Application.Trades;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TradeWriteRequest : IValidatableObject
{
    private string symbol = string.Empty;
    [Required, EnumDataType(typeof(MarketType))]
    public MarketType? MarketType { get; set; }
    [Required, StringLength(30)]
    public string Symbol { get => symbol; set => symbol = value?.Trim().ToUpperInvariant() ?? string.Empty; }
    [Required, EnumDataType(typeof(Direction))]
    public Direction? Direction { get; set; }
    [Required, SqlDecimal(18, 6, Positive = true)]
    public decimal? PositionSize { get; set; }
    [Required]
    public DateTimeOffset? EntryDateTime { get; set; }
    [Required, SqlDecimal(18, 8, Positive = true)]
    public decimal? EntryPrice { get; set; }
    [SqlDecimal(18, 8, Positive = true)]
    public decimal? StopLoss { get; set; }
    [SqlDecimal(18, 8, Positive = true)]
    public decimal? TakeProfit { get; set; }
    public DateTimeOffset? ExitDateTime { get; set; }
    [SqlDecimal(18, 8, Positive = true)]
    public decimal? ExitPrice { get; set; }
    [SqlDecimal(18, 2)]
    public decimal? ResultAmount { get; set; }
    [EnumDataType(typeof(Status))]
    public Status Status { get; set; } = Status.Open;
    public Guid? StrategyId { get; set; }
    public Guid? EntryEmotionId { get; set; }
    public Guid? ExitEmotionId { get; set; }
    [StringLength(1000)]
    public string? EntryReason { get; set; }
    [StringLength(1000)]
    public string? ExitReason { get; set; }
    public bool? PlanRespected { get; set; }
    [StringLength(2000)]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status == Status.Closed && (ExitDateTime is null || ExitPrice is null || ResultAmount is null))
            yield return new("Une clôture exige date, prix et résultat.", new[] { nameof(ExitDateTime), nameof(ExitPrice), nameof(ResultAmount) });
        if (Status == Status.Open && (ExitDateTime is not null || ExitPrice is not null || ResultAmount is not null))
            yield return new("Un trade ouvert ne doit pas contenir de données de clôture.", new[] { nameof(Status) });
        if (ExitDateTime < EntryDateTime)
            yield return new("La sortie ne peut pas précéder l'entrée.", new[] { nameof(ExitDateTime) });
    }
}
