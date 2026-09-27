using TradingJournal.Domain.Entities;

namespace TradingJournal.Domain;

public static class PredefinedEmotions
{
    // Stable identifiers for future persistence; fresh instances avoid shared mutable entities.
    public static IReadOnlyList<Emotion> Create() => new List<Emotion>
    {
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), Name = "Calme" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000002"), Name = "Confiant" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), Name = "Stressé" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000004"), Name = "Impatient" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000005"), Name = "Euphorique" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000006"), Name = "Frustré" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000007"), Name = "Craintif" }
    }.AsReadOnly();
}
