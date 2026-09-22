using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Web.Areas.Admin.Controllers;

/// <summary>Phòng chiếu luôn thuộc một chi nhánh: mọi trang đều đi từ <c>theaterId</c>.</summary>
public class RoomsController : AdminControllerBase
{
    private readonly CinemaDbContext _db;

    public RoomsController(CinemaDbContext db) => _db = db;

    public async Task<IActionResult> Index(int theaterId, CancellationToken ct)
    {
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
        var theater = await _db.Theaters.AsNoTracking().FirstOrDefaultAsync(t => t.Id == theaterId, ct);
        if (theater is null) return NotFound();

        return View("Form", new RoomFormModel { TheaterId = theater.Id, TheaterName = theater.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoomFormModel form, CancellationToken ct)
    {
        // TheaterId đến từ form nên phải kiểm tra có thật (General.md §7.7).
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
        return room is null ? NotFound() : View("Form", RoomFormModel.From(room));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, RoomFormModel form, CancellationToken ct)
    {
        var room = await _db.Rooms.Include(r => r.Theater).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (room is null) return NotFound();

        // Chi nhánh của phòng lấy từ DB, không tin TheaterId trong form: sửa phòng không được "dời" nó sang chi nhánh khác.
        form.Id = id;
        form.TheaterId = room.TheaterId;
        form.TheaterName = room.Theater!.Name;
        form.TotalRows = room.TotalRows;
        form.TotalColumns = room.TotalColumns;
        ModelState.Remove(nameof(form.TotalRows));
        ModelState.Remove(nameof(form.TotalColumns));
        if (!ModelState.IsValid) return View("Form", form);

        room.RoomName = form.RoomName.Trim();
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index), new { theaterId = room.TheaterId });
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
