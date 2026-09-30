using CinemaBooking.Web.Services;

namespace CinemaBooking.Tests;

public class BranchDirectoryTests
{
    [Fact]
    public async Task GetActive_skips_closed_branches_and_sorts_by_city_then_name()
    {
        using var db = TestDatabase.Create();
        var hk = db.SeedRoom("CineSmart Hoàn Kiếm");
        var cg = db.SeedRoom("CineSmart Cầu Giấy");
        var nh = db.SeedRoom("CineSmart Nguyễn Huệ");
        var closed = db.SeedRoom("CineSmart Đã đóng");
        hk.Theater!.City = cg.Theater!.City = closed.Theater!.City = "Hà Nội";
        nh.Theater!.City = "TP. Hồ Chí Minh";
        closed.Theater.IsActive = false;
        await db.SaveChangesAsync();

        var result = await new BranchDirectory(db).GetActiveAsync();

        Assert.Equal(
            ["CineSmart Cầu Giấy", "CineSmart Hoàn Kiếm", "CineSmart Nguyễn Huệ"],
            result.Select(b => b.Name));
        Assert.All(result, b => Assert.Equal(1, b.RoomCount));
    }
}
