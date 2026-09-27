namespace TradingJournal.Domain.Entities;

public sealed class Strategy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public User? User { get; set; }
    public ICollection<Trade> Trades { get; set; } = new List<Trade>();
}
