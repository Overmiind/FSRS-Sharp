using FsrsSharp.Models;
using Xunit;

namespace Tests;

public class CardTests
{
    [Fact]
    public void TestCardDefaults()
    {
        var card = new Card();
        Assert.Equal(State.Learning, card.State);
        Assert.Equal(0, card.Step);
        Assert.True(card.CardId != Guid.Empty);
    }
}
