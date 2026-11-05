namespace CinemaBooking.Web.Services;

/// <summary>Một giờ bắt đầu được gợi ý; <see cref="Snug"/> = ngay sau suất trước (ít khoảng trống nhất).</summary>
public sealed record SlotSuggestion(DateTime StartLocal, DateTime EndLocal, bool Snug);

/// <summary>
/// Gợi ý giờ bắt đầu cho một phòng trong một ngày: các giờ tròn mỗi 30 phút còn trống, cộng với
/// giờ "liền sau" mỗi suất đã có (kết thúc + thời gian dọn phòng, làm tròn lên 5 phút).
/// Hàm thuần, toàn bộ tính theo giờ rạp — không đụng DB để test được.
/// </summary>
public static class ShowtimeSlotSuggester
{
    public static readonly TimeSpan Opening = TimeSpan.FromHours(8);
    public static readonly TimeSpan LastStart = new(23, 30, 0);
    private const int GridMinutes = 30;
    private const int SnapMinutes = 5;

    public static IReadOnlyList<SlotSuggestion> Suggest(
        DateOnly day,
        int durationMinutes,
        IEnumerable<(DateTime StartLocal, DateTime EndLocal)> existing,
        TimeSpan buffer,
        DateTime? notBeforeLocal = null)
    {
        if (durationMinutes <= 0) return [];

        var busy = existing.OrderBy(e => e.StartLocal).ToList();
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var first = dayStart + Opening;
        var last = dayStart + LastStart;
        var duration = TimeSpan.FromMinutes(durationMinutes);

        var candidates = new Dictionary<DateTime, bool>(); // giờ → snug
        for (var t = first; t <= last; t = t.AddMinutes(GridMinutes)) candidates[t] = false;
        foreach (var (_, end) in busy)
        {
            var snug = RoundUp(end + buffer);
            if (snug >= first && snug <= last) candidates[snug] = true;
        }
        // Phòng trống đến giờ mở cửa → giờ mở cửa cũng là một lựa chọn "liền sau".
        candidates[first] = candidates.GetValueOrDefault(first);

        return candidates
            .Where(c => notBeforeLocal is null || c.Key >= notBeforeLocal)
            .Where(c => busy.All(b => !(c.Key < b.EndLocal + buffer && c.Key + duration > b.StartLocal - buffer)))
            .OrderBy(c => c.Key)
            .Select(c => new SlotSuggestion(c.Key, c.Key + duration, c.Value))
            .ToList();
    }

    private static DateTime RoundUp(DateTime t)
    {
        var rem = (int)(t.TimeOfDay.TotalMinutes % SnapMinutes);
        var rounded = rem == 0 && t.Second == 0 ? t : t.AddMinutes(SnapMinutes - rem);
        return new DateTime(rounded.Year, rounded.Month, rounded.Day, rounded.Hour, rounded.Minute, 0);
    }
}
