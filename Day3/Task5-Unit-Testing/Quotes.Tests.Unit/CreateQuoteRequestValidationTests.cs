using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using QuotesApi.Contracts;

namespace Quotes.Tests.Unit;

public sealed class CreateQuoteRequestValidationTests
{
    private static IReadOnlyList<ValidationResult> Validate(CreateQuoteRequest request)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(request);

        Validator.TryValidateObject(request, context, results, validateAllProperties: true);

        return results;
    }

    [Fact]
    public void Validate_WithValidAuthorAndText_ProducesNoValidationErrors()
    {
        // Arrange
        var request = new CreateQuoteRequest("Maya Angelou", "Still I rise.");

        // Act
        var results = Validate(request);

        // Assert
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithMissingAuthor_ProducesRequiredError(string author)
    {
        // Arrange
        var request = new CreateQuoteRequest(author, "Some quote text.");

        // Act
        var results = Validate(request);

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains("Author"));
    }

    [Fact]
    public void Validate_WithAuthorLongerThan100Characters_ProducesStringLengthError()
    {
        // Arrange
        var request = new CreateQuoteRequest(new string('a', 101), "Some quote text.");

        // Act
        var results = Validate(request);

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains("Author"));
    }

    [Fact]
    public void Validate_WithAuthorExactly100Characters_ProducesNoValidationErrors()
    {
        // Arrange
        var request = new CreateQuoteRequest(new string('a', 100), "Some quote text.");

        // Act
        var results = Validate(request);

        // Assert
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithMissingText_ProducesRequiredError(string text)
    {
        // Arrange
        var request = new CreateQuoteRequest("An Author", text);

        // Act
        var results = Validate(request);

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains("Text"));
    }

    [Fact]
    public void Validate_WithTextLongerThan1000Characters_ProducesStringLengthError()
    {
        // Arrange
        var request = new CreateQuoteRequest("An Author", new string('t', 1001));

        // Act
        var results = Validate(request);

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains("Text"));
    }

    [Fact]
    public void Validate_WithTextExactly1000Characters_ProducesNoValidationErrors()
    {
        // Arrange
        var request = new CreateQuoteRequest("An Author", new string('t', 1000));

        // Act
        var results = Validate(request);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithMissingAuthorAndText_ProducesOneErrorPerField()
    {
        // Arrange
        var request = new CreateQuoteRequest("", "");

        // Act
        var results = Validate(request);

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.MemberNames.Contains("Author"));
        results.Should().Contain(r => r.MemberNames.Contains("Text"));
    }
}
