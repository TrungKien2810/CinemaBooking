using CinemaBooking.Domain.Entities;
using CinemaBooking.Domain.Enums;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>
/// Phòng chiếu luôn thuộc một chi nhánh: mọi trang đều đi từ <c>theaterId</c>.
/// <para>
/// BranchAdmin chỉ thấy/sửa được phòng của chi nhánh mình — kiểm tra bằng <see cref="IBranchScope"/>
/// ở MỌI action, không tin <c>theaterId</c> trên URL hay trong form.
/// </para>
/// </summary>
public class RoomsController : AdminControllerBase
{
    private readonly CinemaDbContext _db;
    private readonly IBranchScope _branches;
    private readonly TimeProvider _clock;

    public RoomsController(CinemaDbContext db, IBranchScope branches, TimeProvider? clock = null)
    {
        _db = db;
        _branches = branches;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<IActionResult> Index(int? theaterId, CancellationToken ct)
    {
        var scoped = await _branches.TheaterIdAsync(ct);

        if (scoped is not null)
        {
            // Branch admin: URL có ghi chi nhánh khác thì chặn, không âm thầm đổi sang chi nhánh mình.
            if (theaterId is not null && theaterId != scoped) return Forbid();
            theaterId = scoped;
        }
        else if (theaterId is null)
        {
            // Admin toàn hệ thống: phòng luôn đi từ một chi nhánh, chưa chọn thì về danh sách chi nhánh.
            return RedirectToAction("Index", "Theaters");
        }

        var theater = await _db.Theaters.AsNoTracking().FirstOrDefaultAsync(t => t.Id == theaterId, ct);
        if (theater is null) return NotFound();

        var rooms = await _db.Rooms.AsNoTracking()
            .Where(r => r.TheaterId == theaterId)
            .OrderBy(r => r.RoomName)
            .Select(r => new RoomListItem(
                r.Id, r.RoomName, r.TotalRows, r.TotalColumns,
                r.Seats.Count, _db.Showtimes.Count(s => s.RoomId == r.Id), r.RoomType))
            .ToListAsync(ct);

        return View(new RoomsIndexViewModel(theater.Id, theater.Name, rooms));
    }

    [HttpGet]
    public async Task<IActionResult> Create(int theaterId, CancellationToken ct)
    {
        if (!await _branches.CanAccessTheaterAsync(theaterId, ct)) return Forbid();

        var theater = await _db.Theaters.AsNoTracking().FirstOrDefaultAsync(t => t.Id == theaterId, ct);
        if (theater is null) return NotFound();

        return View("Form", new RoomFormModel { TheaterId = theater.Id, TheaterName = theater.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoomFormModel form, CancellationToken ct)
    {
        // TheaterId đến từ form nên phải kiểm tra có thật và nằm trong phạm vi người dùng (General.md §7.7).
        if (!await _branches.CanAccessTheaterAsync(form.TheaterId, ct)) return Forbid();

        var theater = await _db.Theaters.FirstOrDefaultAsync(t => t.Id == form.TheaterId, ct);
        if (theater is null) return NotFound();

        form.TheaterName = theater.Name;
        await CheckDuplicateNameAsync(form, theater.Id, null, ct);
        if (!ModelState.IsValid) return View("Form", form);

        var room = new Room
        {
            TheaterId = theater.Id,
            RoomName = form.RoomName.Trim(),
            TotalRows = form.TotalRows,
            TotalColumns = form.TotalColumns,
            RoomType = form.RoomType,
        };
        // Gắn ghế qua navigation: EF điền RoomId sau khi insert phòng, gọn trong một SaveChanges.
        foreach (var seat in SeatGenerator.Generate(room, form.LayoutPreset, form.AisleAfterColumn)) room.Seats.Add(seat);

        _db.Rooms.Add(room);
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index), new { theaterId = theater.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var room = await _db.Rooms.AsNoTracking().Include(r => r.Theater).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (room is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(room.TheaterId, ct)) return Forbid();

        return View("Form", RoomFormModel.From(room));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, RoomFormModel form, CancellationToken ct)
    {
        var room = await _db.Rooms.Include(r => r.Theater).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (room is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(room.TheaterId, ct)) return Forbid();

        // Chi nhánh của phòng lấy từ DB, không tin TheaterId trong form: sửa phòng không được "dời" nó sang chi nhánh khác.
        form.Id = id;
        form.TheaterId = room.TheaterId;
        form.TheaterName = room.Theater!.Name;

        var dimensionsChanged = form.TotalRows != room.TotalRows || form.TotalColumns != room.TotalColumns;
        var layoutChanged = dimensionsChanged || form.RegenerateSeats;
        if (layoutChanged && await HasBookedSeatsAsync(room.Id, ct))
        {
            ModelState.AddModelError(string.Empty,
                "Không đổi được sơ đồ ghế: phòng đã có vé được đặt. Chỉ sửa được tên phòng.");
        }
        await CheckDuplicateNameAsync(form, room.TheaterId, id, ct);
        if (!ModelState.IsValid) return View("Form", form);

        room.RoomName = form.RoomName.Trim();
        room.RoomType = form.RoomType;
        if (layoutChanged)
        {
            room.TotalRows = form.TotalRows;
            room.TotalColumns = form.TotalColumns;
            await RegenerateSeatsAsync(room, form.LayoutPreset, form.AisleAfterColumn, ct);
        }
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index), new { theaterId = room.TheaterId });
    }

    /// <summary>Tên phòng phải duy nhất trong chi nhánh (không phân biệt hoa thường theo collation DB).</summary>
    private async Task CheckDuplicateNameAsync(RoomFormModel form, int theaterId, int? excludeId, CancellationToken ct)
    {
        var name = (form.RoomName ?? string.Empty).Trim();
        if (name.Length == 0) return;

        if (await _db.Rooms.AnyAsync(r => r.TheaterId == theaterId && r.RoomName == name && r.Id != excludeId, ct))
            ModelState.AddModelError(nameof(form.RoomName), "Chi nhánh đã có phòng trùng tên này.");
    }

    /// <summary>
    /// Sơ đồ ghế xem trước cho form phòng — form gọi lại mỗi khi admin đổi số hàng/số ghế.
    /// Không đụng DB: chỉ chạy <see cref="SeatGenerator"/> trên một phòng tạm.
    /// </summary>
    [HttpGet]
    public IActionResult SeatPreview(
        int rows, int columns, SeatLayoutPreset preset = SeatLayoutPreset.Classic, int aisle = 0,
        RoomType roomType = RoomType.Standard) =>
        PartialView("_SeatPreview", SeatPreviewModel.For(rows, columns, preset, aisle, roomType));

    // ── Sửa loại ghế (BranchAdmin + Admin) ─────────────────────────────────────

    /// <summary>
    /// Trang sửa loại ghế: chọn công cụ Thường / VIP / Ghế đôi rồi bấm ghế. Ghế đôi ghép hai ghế
    /// liền kề cùng hàng, đặt ở hàng nào cũng được.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Seats(int id, CancellationToken ct)
    {
        var room = await LoadRoomForSeatsAsync(id, ct);
        if (room is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(room.TheaterId, ct)) return Forbid();

        return View(BuildSeatsViewModel(room, await FutureBookedSeatIdsAsync(room.Id, ct)));
    }

    /// <summary>
    /// Lưu loại ghế. <paramref name="types"/> có dạng <c>"12=Vip;13=Standard;14=Couple;…"</c>.
    /// Từ chối cả lần lưu (không lưu dở) nếu: có ghế đôi lẻ loi (phải thành cặp liền kề cùng hàng), hoặc
    /// đổi loại một ghế đang có vé của suất chưa chiếu (khách đã trả giá theo loại ghế cũ).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Seats(int id, string? types, CancellationToken ct)
    {
        var room = await LoadRoomForSeatsAsync(id, ct);
        if (room is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(room.TheaterId, ct)) return Forbid();

        var requested = ParseSeatTypes(types);
        var changes = room.Seats
            .Where(seat => requested.TryGetValue(seat.Id, out var type) && type != seat.SeatType)
            .ToList();

        // Chỉ đụng tới ghế thuộc phòng này (room.Seats) — id lạ trong chuỗi bị bỏ qua.
        var locked = await FutureBookedSeatIdsAsync(room.Id, ct);
        // Ghế đang có vé: vé đã bán giữ nguyên giá (PriceAtBooking) nên vẫn đổi được Thường ↔ VIP cho các suất sau;
        // nhưng KHÔNG ghép/tách ghế đôi — cặp đã bán phải giữ nguyên.
        var lockedChanges = changes
            .Where(seat => locked.Contains(seat.Id)
                && (seat.SeatType == SeatType.Couple || requested[seat.Id] == SeatType.Couple))
            .Select(seat => seat.Label)
            .ToList();
        if (lockedChanges.Count > 0)
        {
            SetError($"Không ghép/tách ghế đôi được ở {string.Join(", ", lockedChanges)}: đang có vé của suất chưa chiếu.");
            return RedirectToAction(nameof(Seats), new { id });
        }

        // Áp thử lên bộ nhớ rồi kiểm ghế đôi trước khi ghi DB. Ghế đôi lẻ loi có SẴN từ trước (phòng cũ
        // tạo khi số cột lẻ) không chặn việc lưu — chỉ chặn lỗi MỚI do lần sửa này gây ra.
        var preexistingOrphans = SeatPairing.OrphanCoupleSeats(room.Seats).Select(seat => seat.Id).ToHashSet();
        var partnersBefore = SeatPairing.BuildPartnerMap(room.Seats);
        var before = changes.ToDictionary(seat => seat.Id, seat => seat.SeatType);
        foreach (var seat in changes) seat.SeatType = requested[seat.Id];

        var layoutError = SeatPairing.ValidateLayout(room.Seats, preexistingOrphans);

        // Ghế đôi đã có vé phải giữ NGUYÊN cặp: đổi ghế bên cạnh có thể làm lệch cách ghép trong đoạn
        // ghế liền kề (ví dụ cặp 3+4 đã bán, thêm ghế đôi ở 2 và 5 thì thành (2,3)(4,5)) — khách giữ vé
        // cặp 3+4 sẽ thành hai nửa của hai cặp khác nhau.
        if (layoutError is null)
        {
            var partnersAfter = SeatPairing.BuildPartnerMap(room.Seats);
            var broken = room.Seats
                .Where(seat => locked.Contains(seat.Id))
                .Where(seat => partnersBefore.GetValueOrDefault(seat.Id, 0) != partnersAfter.GetValueOrDefault(seat.Id, 0))
                .Select(seat => seat.Label)
                .ToList();
            if (broken.Count > 0)
                layoutError = $"Thay đổi này làm đổi cặp ghế đôi đang có vé ({string.Join(", ", broken)}). Hãy giữ nguyên cặp đó.";
        }

        if (layoutError is not null)
        {
            foreach (var seat in changes) seat.SeatType = before[seat.Id];
            SetError(layoutError);
            return RedirectToAction(nameof(Seats), new { id });
        }

        var changed = changes.Count;
        await _db.SaveChangesAsync(ct);

        // TempData chỉ có khi chạy trong pipeline MVC (unit test dựng controller trực tiếp thì null).
        if (TempData is not null)
        {
            TempData["Message"] = changed == 0
                ? "Sơ đồ ghế không thay đổi."
                : $"Đã cập nhật loại {changed} ghế.";
        }

        return RedirectToAction(nameof(Seats), new { id });
    }

    private async Task<Room?> LoadRoomForSeatsAsync(int id, CancellationToken ct) =>
        await _db.Rooms
            .Include(r => r.Theater)
            .Include(r => r.Seats)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    private void SetError(string message)
    {
        // TempData chỉ có khi chạy trong pipeline MVC (unit test dựng controller trực tiếp thì null).
        if (TempData is not null) TempData["Error"] = message;
    }

    /// <summary>Ghế của phòng đang có vé (BookingSeats) của suất chưa chiếu — đổi loại sẽ lệch giá đã bán.</summary>
    private async Task<HashSet<int>> FutureBookedSeatIdsAsync(int roomId, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var ids = await _db.BookingSeats.AsNoTracking()
            .Where(bs => bs.Seat!.RoomId == roomId && bs.Showtime!.StartTimeUtc > nowUtc)
            .Select(bs => bs.SeatId)
            .Distinct()
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    private static RoomSeatsViewModel BuildSeatsViewModel(Room room, IReadOnlySet<int> lockedSeatIds)
    {
        var rows = room.Seats
            .GroupBy(s => s.RowNumber)
            .OrderBy(g => g.Key)
            .Select(g => new RoomSeatRowViewModel(
                // Nhãn hàng giống nhãn ghế: 1 → A, 2 → B… (Seat.Label dùng cùng quy ước).
                ((char)('A' + g.Key - 1)).ToString(),
                g.OrderBy(s => s.ColumnNumber)
                    .Select(s => new RoomSeatCellViewModel(
                        s.Id, s.Label, s.SeatType, s.RowNumber, s.ColumnNumber, lockedSeatIds.Contains(s.Id)))
                    .ToList()))
            .ToList();

        return new RoomSeatsViewModel(
            room.Id, room.TheaterId, room.RoomName, room.Theater?.Name ?? string.Empty,
            room.TotalRows, room.TotalColumns, rows);
    }

    private static Dictionary<int, SeatType> ParseSeatTypes(string? raw)
    {
        var result = new Dictionary<int, SeatType>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        foreach (var pair in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var seatId)) continue;
            if (!Enum.TryParse<SeatType>(parts[1], ignoreCase: true, out var type) || !Enum.IsDefined(type)) continue;
            result[seatId] = type;
        }

        return result;
    }

    /// <summary>
    /// Đã có vé tham chiếu tới ghế (FK Restrict) thì không xóa ghế được, và cũng không được:
    /// khách đang cầm vé ghế C7 mà phòng chỉ còn 5 hàng thì vé đó vô nghĩa.
    /// </summary>
    private Task<bool> HasBookedSeatsAsync(int roomId, CancellationToken ct) =>
        _db.BookingSeats.AnyAsync(bs => bs.Seat!.RoomId == roomId, ct);

    /// <summary>
    /// Xóa toàn bộ ghế cũ rồi sinh lại theo layout mới, thay vì thêm/bớt từng hàng: quy ước
    /// loại ghế mặc định (VIP ở giữa, ghế đôi hàng cuối) phụ thuộc tổng số hàng nên đổi layout là đổi cả sơ đồ.
    /// </summary>
    private async Task RegenerateSeatsAsync(Room room, SeatLayoutPreset preset, int aisleAfterColumn, CancellationToken ct)
    {
        var oldSeats = await _db.Seats.Where(s => s.RoomId == room.Id).ToListAsync(ct);
        _db.Seats.RemoveRange(oldSeats);
        _db.Seats.AddRange(SeatGenerator.Generate(room, preset, aisleAfterColumn));
    }

    /// <summary>
    /// Xóa hẳn, nhưng chỉ khi phòng chưa từng có suất chiếu: suất chiếu kéo theo đơn đặt vé,
    /// xóa phòng lúc đó sẽ làm mất lịch sử. Phòng đã có suất thì giữ lại.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (room is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(room.TheaterId, ct)) return Forbid();

        if (await _db.Showtimes.AnyAsync(s => s.RoomId == id, ct))
        {
            TempData["Error"] = $"Không xóa được {room.RoomName}: phòng đã có suất chiếu.";
            return RedirectToAction(nameof(Index), new { theaterId = room.TheaterId });
        }

        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index), new { theaterId = room.TheaterId });
    }
}
