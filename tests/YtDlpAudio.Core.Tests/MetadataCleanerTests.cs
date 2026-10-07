using Xunit;
using YtDlpAudio.Infrastructure.Process;

namespace YtDlpAudio.Core.Tests;

public class MetadataCleanerTests
{
    private readonly MetadataCleaner _cleaner = new();

    [Theory]
    [InlineData("Queen - Bohemian Rhapsody (Official Video)", "Queen - Bohemian Rhapsody")]
    [InlineData("Daft Punk - Get Lucky (Official Audio)", "Daft Punk - Get Lucky")]
    [InlineData("Nirvana - Smells Like Teen Spirit [Remastered 2021]", "Nirvana - Smells Like Teen Spirit")]
    [InlineData("The Weeknd - Blinding Lights (Lyric Video) [4K UHD]", "The Weeknd - Blinding Lights")]
    [InlineData("Michael Jackson - Billie Jean (Audio)", "Michael Jackson - Billie Jean")]
    [InlineData("Coldplay - Yellow [Official Visualizer]", "Coldplay - Yellow")]
    public void CleanTitle_RemovesClutter(string input, string expected)
    {
        string actual = _cleaner.CleanTitle(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Gorillaz - Topic", "Gorillaz")]
    [InlineData("DaftPunkVEVO", "DaftPunk")]
    [InlineData("Radiohead", "Radiohead")]
    public void CleanArtist_RemovesTopicAndVevoSuffixes(string input, string expected)
    {
        string actual = _cleaner.CleanArtist(input);
        Assert.Equal(expected, actual);
    }
}
