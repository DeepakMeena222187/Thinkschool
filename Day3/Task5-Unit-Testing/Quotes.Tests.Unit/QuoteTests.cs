using FluentAssertions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public sealed class QuoteTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsQuoteWithTrimmedFieldsAndSuppliedTimestamp()
    {
        // Arrange
        var author = "  Marcus Aurelius  ";
        var text = "  You have power over your mind.  ";
        var ownerId = 7;
        var createdAtUtc = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var quote = Quote.Create(author, text, ownerId, createdAtUtc);

        // Assert
        quote.Author.Should().Be("Marcus Aurelius");
        quote.Text.Should().Be("You have power over your mind.");
        quote.OwnerId.Should().Be(ownerId);
        quote.CreatedAtUtc.Should().Be(createdAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingAuthor_ThrowsArgumentException(string? author)
    {
        // Arrange
        var action = () => Quote.Create(author!, "Some quote text.", 1, DateTime.UtcNow);

        // Act & Assert
        action.Should().Throw<ArgumentException>()
            .WithMessage("Author is required.*")
            .And.ParamName.Should().Be("author");
    }

    [Fact]
    public void Create_WithAuthorLongerThan100Characters_ThrowsArgumentException()
    {
        // Arrange
        var author = new string('a', 101);
        var action = () => Quote.Create(author, "Some quote text.", 1, DateTime.UtcNow);

        // Act & Assert
        action.Should().Throw<ArgumentException>()
            .WithMessage("Author must be at most 100 characters.*")
            .And.ParamName.Should().Be("author");
    }

    [Fact]
    public void Create_WithAuthorExactly100Characters_Succeeds()
    {
        // Arrange
        var author = new string('a', 100);

        // Act
        var quote = Quote.Create(author, "Some quote text.", 1, DateTime.UtcNow);

        // Assert
        quote.Author.Should().HaveLength(100);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingText_ThrowsArgumentException(string? text)
    {
        // Arrange
        var action = () => Quote.Create("An Author", text!, 1, DateTime.UtcNow);

        // Act & Assert
        action.Should().Throw<ArgumentException>()
            .WithMessage("Text is required.*")
            .And.ParamName.Should().Be("text");
    }

    [Fact]
    public void Create_WithTextLongerThan1000Characters_ThrowsArgumentException()
    {
        // Arrange
        var text = new string('t', 1001);
        var action = () => Quote.Create("An Author", text, 1, DateTime.UtcNow);

        // Act & Assert
        action.Should().Throw<ArgumentException>()
            .WithMessage("Text must be at most 1000 characters.*")
            .And.ParamName.Should().Be("text");
    }

    [Fact]
    public void Create_WithTextExactly1000Characters_Succeeds()
    {
        // Arrange
        var text = new string('t', 1000);

        // Act
        var quote = Quote.Create("An Author", text, 1, DateTime.UtcNow);

        // Assert
        quote.Text.Should().HaveLength(1000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveOwnerId_ThrowsArgumentException(int ownerId)
    {
        // Arrange
        var action = () => Quote.Create("An Author", "Some quote text.", ownerId, DateTime.UtcNow);

        // Act & Assert
        action.Should().Throw<ArgumentException>()
            .WithMessage("OwnerId must be greater than zero.*")
            .And.ParamName.Should().Be("ownerId");
    }
}
