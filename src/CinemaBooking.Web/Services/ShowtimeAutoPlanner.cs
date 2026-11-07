namespace CinemaBooking.Web.Services;

public sealed record PlanRoom(int RoomId, string Name, int Capacity);

public sealed record PlanMovie(int MovieId, string Title, int DurationMinutes, DateOnly ReleaseDate);

/// <summary>Một suất do bộ xếp lịch đề xuất. <see cref="Prime"/> = giờ vàng (khách đông, giá cao hơn).</summary>
public sealed record PlannedShowtime(
    int RoomId, string RoomName, int MovieId, string MovieTitle,
    DateTime StartLocal, DateTime EndLocal, bool Prime);

/// <summary>
/// Đề xuất lịch chiếu cả ngày cho MỘT chi nhánh. Thuần hàm (không đụng DB) để test được.
/// <para>
/// Cách rạp thật làm: phim nào được chiếu ở chi nhánh do hợp đồng với nhà phát hành/trụ sở quyết, còn
/// QUẢN LÝ RẠP xếp phim vào phòng và giờ. Nguyên tắc xếp quen thuộc, được cài ở đây:
/// </para>
/// <list type="bullet">
/// <item>Phim mới/hot (ngày khởi chiếu gần) chiếu nhiều hơn và vào phòng lớn nhất, giờ vàng (18:00–22:00).</item>
/// <item>Mỗi phòng một phim khác nhau ở giờ vàng — không để hai phòng chiếu cùng phim cùng lúc nếu còn phim khác.</item>
/// <item>Ngoài giờ vàng, xoay vòng phim (ưu tiên phim ít suất nhất trong phòng) và không chiếu cùng phim hai suất liền nhau.</item>
/// <item>Các suất nối nhau sát giờ dọn phòng, làm tròn 5 phút; suất cuối bắt đầu không muộn quá 23:30.</item>
/// </list>
/// </summary>
public static class ShowtimeAutoPlanner
{
    public static readonly TimeSpan Opening = TimeSpan.FromHours(9);
    public static readonly TimeSpan LastStart = new(23, 30, 0);
    public static readonly TimeSpan PrimeFrom = TimeSpan.FromHours(18);
    public static readonly TimeSpan PrimeTo = TimeSpan.FromHours(22);
    private const int SnapMinutes = 5;

    /// <param name="existing">Suất đã có trong ngày theo phòng (giờ rạp) — tránh trùng, kể cả thời gian dọn phòng.</param>
    /// <param name="notBeforeLocal">Không xếp suất bắt đầu trước mốc này (hôm nay thì là giờ hiện tại).</param>
    public static IReadOnlyList<PlannedShowtime> Plan(
        DateOnly day,
        IReadOnlyList<PlanRoom> rooms,
        IReadOnlyList<PlanMovie> movies,
        IReadOnlyDictionary<int, IReadOnlyList<(DateTime StartLocal, DateTime EndLocal)>> existing,
        TimeSpan buffer,
        DateTime? notBeforeLocal = null)
    {
        if (rooms.Count == 0 || movies.Count == 0) return [];

        // Phim hot trước: khởi chiếu càng gần càng được ưu tiên; hòa thì phim ngắn trước (dễ xếp nhiều suất).
        var ranked = movies
            .OrderByDescending(m => m.ReleaseDate)
            .ThenBy(m => m.DurationMinutes)
            .ThenBy(m => m.MovieId)
            .ToList();
        var orderedRooms = rooms.OrderByDescending(r => r.Capacity).ThenBy(r => r.Name).ToList();

        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var plan = new List<PlannedShowtime>();

        for (var i = 0; i < orderedRooms.Count; i++)
        {
            var room = orderedRooms[i];
            var busy = existing.TryGetValue(room.RoomId, out var list)
                ? list.OrderBy(b => b.StartLocal).ToList()
                : [];
            var shown = new Dictionary<int, int>(); // movieId → số suất đã xếp trong phòng này
            PlanMovie? previous = null;

            var cursor = dayStart + Opening;
            if (notBeforeLocal is { } nb && nb > cursor) cursor = RoundUp(nb);

            while (cursor <= dayStart + LastStart)
            {
                var prime = IsPrime(cursor);
                var movie = prime
                    ? PickPrime(ranked, i, previous)
                    : PickFiller(ranked, i, shown, previous);

                var end = cursor.AddMinutes(movie.DurationMinutes);

                // Suất ngay trước giờ vàng không được chiếu đúng phim của giờ vàng: nếu không, suất giờ
                // vàng (quan trọng nhất) sẽ bị luật "không lặp liền nhau" đẩy sang phim kém hot hơn.
                if (!prime && ranked.Count > 1 && IsPrime(RoundUp(end + buffer)) && movie.MovieId == ranked[i % ranked.Count].MovieId)
                {
                    movie = PickFiller(ranked, i, shown, previous, exclude: movie.MovieId);
                    end = cursor.AddMinutes(movie.DurationMinutes);
                }

                // Đụng suất có sẵn (kể cả thời gian dọn phòng) thì nhảy tới sau suất đó rồi thử lại.
                var clash = busy.FirstOrDefault(b => cursor < b.EndLocal + buffer && end > b.StartLocal - buffer);
                if (clash != default)
                {
                    cursor = RoundUp(clash.EndLocal + buffer);
                    continue;
                }

                plan.Add(new PlannedShowtime(room.RoomId, room.Name, movie.MovieId, movie.Title, cursor, end, prime));
                shown[movie.MovieId] = shown.GetValueOrDefault(movie.MovieId) + 1;
                previous = movie;
                cursor = RoundUp(end + buffer);
            }
        }

        return plan.OrderBy(p => p.StartLocal).ThenBy(p => p.RoomName).ToList();
    }

    /// <summary>Giờ vàng: phòng hạng i nhận phim hạng i (phòng lớn nhất ↔ phim hot nhất), tránh lặp phim vừa chiếu.</summary>
    private static PlanMovie PickPrime(List<PlanMovie> ranked, int roomRank, PlanMovie? previous)
    {
        var pick = ranked[roomRank % ranked.Count];
        if (previous is not null && pick.MovieId == previous.MovieId && ranked.Count > 1)
            pick = ranked[(roomRank + 1) % ranked.Count];
        return pick;
    }

    private static bool IsPrime(DateTime t) => t.TimeOfDay >= PrimeFrom && t.TimeOfDay < PrimeTo;

    /// <summary>
    /// Ngoài giờ vàng: phim ít suất nhất trong phòng (hòa thì theo thứ hạng, lệch theo phòng), không lặp phim liền trước.
    /// <paramref name="exclude"/> là yêu cầu mềm: nếu loại nó đi mà không còn phim nào thì bỏ qua yêu cầu đó.
    /// </summary>
    private static PlanMovie PickFiller(
        List<PlanMovie> ranked, int roomRank, Dictionary<int, int> shown, PlanMovie? previous, int? exclude = null)
    {
        var n = ranked.Count;
        var candidates = Enumerable.Range(0, n)
            .Select(k => ranked[(roomRank + k) % n])
            .Where(m => n == 1 || previous is null || m.MovieId != previous.MovieId)
            .ToList();

        var preferred = candidates.Where(m => exclude is null || m.MovieId != exclude).ToList();
        return (preferred.Count > 0 ? preferred : candidates)
            .OrderBy(m => shown.GetValueOrDefault(m.MovieId))
            .First();
    }

    private static DateTime RoundUp(DateTime t)
    {
        var rem = (int)(t.TimeOfDay.TotalMinutes % SnapMinutes);
        var rounded = rem == 0 && t.Second == 0 ? t : t.AddMinutes(SnapMinutes - rem);
        return new DateTime(rounded.Year, rounded.Month, rounded.Day, rounded.Hour, rounded.Minute, 0);
    }
}
