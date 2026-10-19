using CinemaBooking.Web.Controllers;
using CinemaBooking.Web.Models;
using CinemaBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace CinemaBooking.Tests;

public class HomeIndexTests
{
    [Fact]
    public async Task Index_passes_search_and_genre_through_and_lists_every_genre()
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Dune", genre: "Khoa học viễn tưởng");
        db.SeedMovie("Lật Mặt 7", genre: "Gia đình");
        db.SeedMovie("Doraemon", genre: "Hoạt hình");
        await db.SaveChangesAsync();

        var sut = new HomeController(new MovieCatalogService(db), NullLogger<HomeController>.Instance);

        var view = Assert.IsType<ViewResult>(await sut.Index("  do  ", "hoạt hình", default));
        var model = Assert.IsType<HomeIndexViewModel>(view.Model);

        Assert.Equal("Doraemon", Assert.Single(model.Movies).Title);
        Assert.Equal("do", model.Keyword);           // đã trim để điền lại vào ô tìm
        Assert.True(model.IsFiltered);
        Assert.Equal(3, model.GenreOptions.Count);   // chip vẫn đủ thể loại dù đang lọc
    }

    [Fact]
    public async Task Index_without_filters_is_not_marked_filtered()
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Dune");
        await db.SaveChangesAsync();

        var sut = new HomeController(new MovieCatalogService(db), NullLogger<HomeController>.Instance);

        var model = Assert.IsType<HomeIndexViewModel>(Assert.IsType<ViewResult>(await sut.Index(" ", null, default)).Model);

        Assert.False(model.IsFiltered);
        Assert.Single(model.Movies);
    }
}
