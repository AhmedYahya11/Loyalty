using Loyalty.Core.Modules.Cvm.Domain;
using Shouldly;

namespace Loyalty.Tests.Unit;

public class StreakTests
{
    private static MemberValueProfile NewProfile() =>
        MemberValueProfile.Create(Guid.NewGuid(), Guid.NewGuid(), "IQD", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static DateTimeOffset Day(int d) => new(2026, 6, d, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Consecutive_days_increment_streak()
    {
        var p = NewProfile();
        p.ApplyStreak(Day(1)); p.CurrentStreak.ShouldBe(1);
        p.ApplyStreak(Day(2)); p.CurrentStreak.ShouldBe(2);
        p.ApplyStreak(Day(3)); p.CurrentStreak.ShouldBe(3);
        p.LongestStreak.ShouldBe(3);
    }

    [Fact]
    public void Same_day_does_not_double_count()
    {
        var p = NewProfile();
        p.ApplyStreak(Day(1));
        p.ApplyStreak(new DateTimeOffset(2026, 6, 1, 20, 0, 0, TimeSpan.Zero)); // same day, later
        p.CurrentStreak.ShouldBe(1);
    }

    [Fact]
    public void A_gap_resets_streak_but_keeps_longest()
    {
        var p = NewProfile();
        p.ApplyStreak(Day(1)); p.ApplyStreak(Day(2)); p.ApplyStreak(Day(3)); // streak 3
        p.ApplyStreak(Day(6)); // gap
        p.CurrentStreak.ShouldBe(1);
        p.LongestStreak.ShouldBe(3);
    }

    [Fact]
    public void Out_of_order_older_event_is_ignored()
    {
        var p = NewProfile();
        p.ApplyStreak(Day(5));
        p.ApplyStreak(Day(2)); // delayed/older
        p.CurrentStreak.ShouldBe(1);
        p.LastStreakDate.ShouldBe(new DateOnly(2026, 6, 5));
    }
}
