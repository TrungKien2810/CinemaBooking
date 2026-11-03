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

    public RoomsController(CinemaDbContext db, IBranchScope branches)
    {
        _db = db;
        _branches = branches;
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
                r.Seats.Count, _db.Showtimes.Count(s => s.RoomId == r.Id)))
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
        if (!ModelState.IsValid) return View("Form", form);

        var room = new Room
        {
            TheaterId = theater.Id,
            RoomName = form.RoomName.Trim(),
            TotalRows = form.TotalRows,
            TotalColumns = form.TotalColumns,
        };
        // Gắn ghế qua navigation: EF điền RoomId sau khi insert phòng, gọn trong một SaveChanges.
        foreach (var seat in SeatGenerator.Generate(room)) room.Seats.Add(seat);

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

        var layoutChanged = form.TotalRows != room.TotalRows || form.TotalColumns != room.TotalColumns;
        if (layoutChanged && await HasBookedSeatsAsync(room.Id, ct))
        {
            ModelState.AddModelError(string.Empty,
                "Không đổi được sơ đồ ghế: phòng đã có vé được đặt. Chỉ sửa được tên phòng.");
        }
        if (!ModelState.IsValid) return View("Form", form);

        room.RoomName = form.RoomName.Trim();
        if (layoutChanged)
        {
            room.TotalRows = form.TotalRows;
            room.TotalColumns = form.TotalColumns;
            await RegenerateSeatsAsync(room, ct);
        }
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index), new { theaterId = room.TheaterId });
    }

    /// <summary>
    /// Sơ đồ ghế xem trước cho form phòng — form gọi lại mỗi khi admin đổi số hàng/số ghế.
    /// Không đụng DB: chỉ chạy <see cref="SeatGenerator"/> trên một phòng tạm.
    /// </summary>
    [HttpGet]
    public IActionResult SeatPreview(int rows, int columns) =>
        PartialView("_SeatPreview", SeatPreviewModel.For(rows, columns));

    // ── Sửa loại ghế (BranchAdmin + Admin) ─────────────────────────────────────

    /// <summary>
    /// Trang sửa loại ghế: hàng cuối luôn là ghế đôi (khóa), các hàng khác bấm để đổi Thường ↔ VIP.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Seats(int id, CancellationToken ct)
    {
        var room = await LoadRoomForSeatsAsync(id, ct);
        if (room is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(room.TheaterId, ct)) return Forbid();

        return View(BuildSeatsViewModel(room));
    }

    /// <summary>
    /// Lưu loại ghế. <paramref name="types"/> có dạng <c>"12=Vip;13=Standard;…"</c>; ghế ở hàng cuối
    /// luôn bị ép về Couple và không hàng nào khác được đặt Couple — quy ước "ghế đôi tự động hàng cuối".
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Seats(int id, string? types, CancellationToken ct)
    {
        var room = await LoadRoomForSeatsAsync(id, ct);
        if (room is null) return NotFound();
        if (!await _branches.CanAccessTheaterAsync(room.TheaterId, ct)) return Forbid();

        var requested = ParseSeatTypes(types);
        var changed = 0;

        foreach (var seat in room.Seats)
        {
            var isLastRow = seat.RowNumber == room.TotalRows;

            if (isLastRow)
            {
                if (seat.SeatType != SeatType.Couple)
                {
                    seat.SeatType = SeatType.Couple;
                    changed++;
                }
                continue;
            }

            // Chỉ nhận Thường/VIP ở các hàng trên; dữ liệu rác (Couple, số lạ) bị bỏ qua.
            if (!requested.TryGetValue(seat.Id, out var type) || type is not (SeatType.Standard or SeatType.Vip))
                continue;

            if (seat.SeatType == type) continue;
            seat.SeatType = type;
            changed++;
        }

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

    private static RoomSeatsViewModel BuildSeatsViewModel(Room room)
    {
        var rows = room.Seats
            .GroupBy(s => s.RowNumber)
            .OrderBy(g => g.Key)
            .Select(g => new RoomSeatRowViewModel(
                // Nhãn hàng giống nhãn ghế: 1 → A, 2 → B… (Seat.Label dùng cùng quy ước).
                ((char)('A' + g.Key - 1)).ToString(),
                IsCoupleRow: g.Key == room.TotalRows,
                g.OrderBy(s => s.ColumnNumber)
                    .Select(s => new RoomSeatCellViewModel(s.Id, s.Label, s.SeatType))
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
            if (!Enum.TryParse<SeatType>(parts[1], ignoreCase: true, out var type)) continue;
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
    private async Task RegenerateSeatsAsync(Room room, CancellationToken ct)
    {
        var oldSeats = await _db.Seats.Where(s => s.RoomId == room.Id).ToListAsync(ct);
        _db.Seats.RemoveRange(oldSeats);
        _db.Seats.AddRange(SeatGenerator.Generate(room));
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
