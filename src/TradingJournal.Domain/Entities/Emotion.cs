namespace TradingJournal.Domain.Entities;

public sealed class Emotion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
