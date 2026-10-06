using Runewake.Engine.Supabase;
using Xunit;

namespace Runewake.Tests.Supabase;

/// <summary>FABLE-054: usernames — the shape rules and the word filter.</summary>
public class UsernameTests
{
    [Theory]
    [InlineData("Fictive")] [InlineData("Adam")] [InlineData("Trikzos")] [InlineData("Cassandra")] [InlineData("Scunthorpe")]
    [InlineData("Dickens")] [InlineData("Peacock")] [InlineData("Raccoon")] [InlineData("Analyst")] [InlineData("Therapist")]
    [InlineData("Grape Ape")] [InlineData("Torpedo")] [InlineData("Thorny Rose")] [InlineData("Swanky")] [InlineData("Bass Player")]
    [InlineData("Hancock")] [InlineData("Sussex Knight")] [InlineData("Night_Owl-7")] [InlineData("Glasses")] [InlineData("Assassin")]
    public void Ordinary_names_pass(string name) => Assert.Null(Usernames.Problem(name));

    [Theory]
    [InlineData("fuck")] [InlineData("F_U_C_K")] [InlineData("Fuuuuck")] [InlineData("Sh1thead")] [InlineData("big ass")]
    [InlineData("A s s")] [InlineData("N1gger")] [InlineData("xX_cunt_Xx")] [InlineData("Dick")] [InlineData("cum lord")]
    [InlineData("Bitch Please")] [InlineData("PornStar")] [InlineData("H1tler")] [InlineData("rape")] [InlineData("Wh0re")]
    public void Crude_names_are_blocked(string name) => Assert.Equal("Please pick a different name.", Usernames.Problem(name));

    [Theory]
    [InlineData("ab", "At least 3 characters.")]
    [InlineData("abcdefghijklmnopq", "At most 16 characters.")]
    [InlineData("hi there!", "Letters, numbers, spaces, _ and - only.")]
    [InlineData("_edge", "Start and end with a letter or number.")]
    [InlineData("a  b", "No double spaces.")]
    [InlineData("1234", "Use at least one letter.")]
    public void Shape_rules(string name, string why) => Assert.Equal(why, Usernames.Problem(name));

    [Fact]
    public void Emails_are_masked() => Assert.Equal("t•••@gmail.com", Usernames.MaskEmail("trikzos@gmail.com"));
}
