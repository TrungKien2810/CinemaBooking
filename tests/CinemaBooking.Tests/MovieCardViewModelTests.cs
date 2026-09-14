using CinemaBooking.Web.Models;

namespace CinemaBooking.Tests;

public class MovieCardViewModelTests
{
    private static MovieCardViewModel Card(string? poster = "/img/posters/mat-biec.jpg", string? genre = "Tình cảm") =>
        new(1, "Mắt Biếc", genre, 117, new DateOnly(2019, 12, 20), poster);

    [Fact]
    public void PosterOrPlaceholder_keeps_the_url_when_the_movie_has_one()
    {
        Assert.Equal("/img/posters/mat-biec.jpg", Card().PosterOrPlaceholder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PosterOrPlaceholder_falls_back_when_the_url_is_missing(string? poster)
    {
        Assert.Equal(MovieCardViewModel.PlaceholderPoster, Card(poster: poster).PosterOrPlaceholder);
    }

    [Fact]
    public void GenreOrDefault_keeps_the_genre_when_the_movie_has_one()
    {
        Assert.Equal("Tình cảm", Card().GenreOrDefault);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GenreOrDefault_falls_back_when_the_genre_is_missing(string? genre)
    {
        Assert.Equal("Chưa phân loại", Card(genre: genre).GenreOrDefault);
    }

    [Fact]
    public void HasMovies_is_false_for_an_empty_catalog()
    {
        Assert.False(new HomeIndexViewModel([]).HasMovies);
        Assert.True(new HomeIndexViewModel([Card()]).HasMovies);
    }
}
