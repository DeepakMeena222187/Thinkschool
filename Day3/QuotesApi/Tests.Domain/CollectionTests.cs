using FluentAssertions;
using QuotesApi.Models;
using Xunit;

namespace Tests.Domain;

public class CollectionTests
{
    [Fact]
    public void Empty_name_throws()
    {
        Action act = () => new Collection("   ", 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*required*");
    }

    [Fact]
    public void Name_longer_than_80_characters_throws()
    {
        var longName = new string('x', 81);

        Action act = () => new Collection(longName, 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*between 3 and 80*");
    }

    [Fact]
    public void Adding_the_51st_item_throws()
    {
        var collection = new Collection("Reading List", 1);

        for (var i = 1; i <= 50; i++)
        {
            collection.AddItem(i);
        }

        Action act = () => collection.AddItem(51);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at most 50*");
    }

    [Fact]
    public void Adding_a_duplicate_quote_id_throws()
    {
        var collection = new Collection("Reading List", 1);
        collection.AddItem(7);

        Action act = () => collection.AddItem(7);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate*");
    }

    [Fact]
    public void Removing_a_non_existent_item_throws()
    {
        var collection = new Collection("Reading List", 1);

        Action act = () => collection.RemoveItem(7);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not in the collection*");
    }

    [Fact]
    public void Adding_an_item_and_then_removing_it_leaves_zero_items()
    {
        var collection = new Collection("Reading List", 1);
        collection.AddItem(7);

        collection.RemoveItem(7);

        collection.Items.Should().BeEmpty();
    }
}
