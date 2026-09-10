using CinemaBooking.Web.Services;

namespace CinemaBooking.Tests;

public class MovieCatalogServiceTests
{
    [Fact]
    public async Task Search_without_criteria_returns_only_active_movies()
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Dune");
        db.SeedMovie("Phim đã gỡ", isActive: false);
        await db.SaveChangesAsync();

        var result = await new MovieCatalogService(db).SearchAsync(MovieSearchQuery.All);

        Assert.Equal("Dune", Assert.Single(result).Title);
    }

    [Theory]
    [InlineData("dune")]
    [InlineData("DUNE")]
    [InlineData("Du")]
    public async Task Search_by_keyword_ignores_case_and_matches_partial_title(string keyword)
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Dune");
        db.SeedMovie("Mai");
        await db.SaveChangesAsync();

        var result = await new MovieCatalogService(db).SearchAsync(new MovieSearchQuery(Keyword: keyword));

        Assert.Equal("Dune", Assert.Single(result).Title);
    }

    [Fact]
    public async Task Search_by_keyword_also_matches_genre()
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Dune", genre: "Khoa học viễn tưởng");
        db.SeedMovie("Mai", genre: "Tâm lý");
        await db.SaveChangesAsync();

        var result = await new MovieCatalogService(db).SearchAsync(new MovieSearchQuery(Keyword: "viễn tưởng"));

        Assert.Equal("Dune", Assert.Single(result).Title);
    }

    [Fact]
    public async Task Search_by_genre_is_exact_match_not_substring()
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Dune", genre: "Hành động");
        db.SeedMovie("Mai", genre: "Hành động - Tâm lý");
        await db.SaveChangesAsync();

        var result = await new MovieCatalogService(db).SearchAsync(new MovieSearchQuery(Genre: "hành động"));

        Assert.Equal("Dune", Assert.Single(result).Title);
    }

    [Fact]
    public async Task Search_combines_keyword_and_genre_with_and()
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Dune", genre: "Hành động");
        db.SeedMovie("Dune: Part Two", genre: "Tâm lý");
        db.SeedMovie("Mai", genre: "Hành động");
        await db.SaveChangesAsync();

        var result = await new MovieCatalogService(db)
            .SearchAsync(new MovieSearchQuery(Keyword: "dune", Genre: "Hành động"));

        Assert.Equal("Dune", Assert.Single(result).Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_treats_blank_criteria_as_no_filter(string? blank)
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Dune");
        db.SeedMovie("Mai");
        await db.SaveChangesAsync();

        var result = await new MovieCatalogService(db)
            .SearchAsync(new MovieSearchQuery(Keyword: blank, Genre: blank));

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Search_trims_surrounding_whitespace_from_keyword()
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Dune");
        await db.SaveChangesAsync();

        var result = await new MovieCatalogService(db).SearchAsync(new MovieSearchQuery(Keyword: "  dune  "));

        Assert.Single(result);
    }

    [Fact]
    public async Task Search_orders_by_release_date_descending_then_title()
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Cũ", releaseDate: new DateOnly(2025, 1, 1));
        db.SeedMovie("Mới B", releaseDate: new DateOnly(2026, 5, 1));
        db.SeedMovie("Mới A", releaseDate: new DateOnly(2026, 5, 1));
        await db.SaveChangesAsync();

        var result = await new MovieCatalogService(db).SearchAsync(MovieSearchQuery.All);

        Assert.Equal(new[] { "Mới A", "Mới B", "Cũ" }, result.Select(m => m.Title));
    }

    [Fact]
    public async Task GetGenres_returns_distinct_non_empty_genres_of_active_movies()
    {
        using var db = TestDatabase.Create();
        db.SeedMovie("Dune", genre: "Hành động");
        db.SeedMovie("Mai", genre: "Hành động");
        db.SeedMovie("Đào, phở và piano", genre: "Tâm lý");
        db.SeedMovie("Không rõ thể loại", genre: null);
        db.SeedMovie("Rỗng", genre: string.Empty);
        db.SeedMovie("Đã gỡ", genre: "Kinh dị", isActive: false);
        await db.SaveChangesAsync();

        var genres = await new MovieCatalogService(db).GetGenresAsync();

        Assert.Equal(new[] { "Hành động", "Tâm lý" }, genres);
    }
}
