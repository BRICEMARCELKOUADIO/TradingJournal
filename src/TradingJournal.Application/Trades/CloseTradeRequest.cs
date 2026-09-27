using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TradingJournal.Application.Trades;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CloseTradeRequest
{
    [Required]
    public DateTimeOffset? ExitDateTime { get; set; }
    [Required, SqlDecimal(18, 8, Positive = true)]
    public decimal? ExitPrice { get; set; }
    [Required, SqlDecimal(18, 2)]
    public decimal? ResultAmount { get; set; }
    public Guid? ExitEmotionId { get; set; }
    [StringLength(1000)]
    public string? ExitReason { get; set; }
    public bool? PlanRespected { get; set; }
    [StringLength(2000)]
    public string? Notes { get; set; }
}
