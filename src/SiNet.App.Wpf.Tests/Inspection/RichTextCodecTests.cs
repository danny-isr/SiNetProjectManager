using SiNet.Application.Inspection;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

public sealed class RichTextCodecTests
{
    [Fact]
    public void Parse_and_Encode_round_trip_preserves_styled_runs()
    {
        const string encoded = "Normal {1! critical} and {2 blue}";
        var (plain, runs) = RichTextCodec.Parse(encoded);

        Assert.Equal("Normal critical and blue", plain);
        Assert.Equal(2, runs.Count);
        Assert.Equal(RichTextCodec.RichTextColor.Red, runs[0].Color);
        Assert.True(runs[0].Bold);

        var again = RichTextCodec.Encode(plain, runs);
        var (plain2, runs2) = RichTextCodec.Parse(again);
        Assert.Equal(plain, plain2);
        Assert.Equal(runs.Count, runs2.Count);
        Assert.Equal(runs[0].Color, runs2[0].Color);
        Assert.Equal(runs[0].Bold, runs2[0].Bold);
    }

    [Fact]
    public void ApplyColor_paints_selected_span_and_resolves_overlap()
    {
        const string encoded = "abcdefgh";
        var (plain, runs, map) = RichTextCodec.ParseWithMap(encoded);
        Assert.Empty(runs);

        var start = RichTextCodec.MapEncodedToPlain(map, 2, searchForward: true);
        var end = RichTextCodec.MapEncodedToPlain(map, 5, searchForward: false);
        var length = end - start;

        var updated = RichTextCodec.ApplyColor(
            runs, start, length, RichTextCodec.RichTextColor.Blue, newBold: true);
        var result = RichTextCodec.Encode(plain, updated);

        Assert.Contains("{2!", result, StringComparison.Ordinal);
        Assert.Contains("cde", result, StringComparison.Ordinal);

        var (plain2, runs2) = RichTextCodec.Parse(result);
        Assert.Equal(plain, plain2);
        Assert.Single(runs2);
        Assert.Equal(RichTextCodec.RichTextColor.Blue, runs2[0].Color);
        Assert.True(runs2[0].Bold);
    }

    [Fact]
    public void ApplyColor_with_default_strips_formatting()
    {
        const string encoded = "{1! critical}";
        var (plain, runs) = RichTextCodec.Parse(encoded);
        var stripped = RichTextCodec.ApplyColor(
            runs, 0, plain.Length, RichTextCodec.RichTextColor.Default, newBold: false);
        var result = RichTextCodec.Encode(plain, stripped);

        Assert.Equal("critical", result);
        Assert.Empty(stripped);
    }

    [Fact]
    public void Full_operator_matrix_round_trips_colors_bold_multiline_and_mixed_scripts()
    {
        // RTL Hebrew + English + numbers + multiline + all colors + bold + default
        const string encoded =
            "שורה1 HE mix 42\n" +
            "{1 אדום} {2 כחול} {4 ירוק} {3 אפור}\n" +
            "{1! בולט-אדום} {! bold-only} plain default\n" +
            "long-" + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";

        var (plain, runs) = RichTextCodec.Parse(encoded);
        Assert.Contains("שורה1", plain, StringComparison.Ordinal);
        Assert.Contains("HE mix 42", plain, StringComparison.Ordinal);
        Assert.Contains('\n', plain);
        Assert.True(runs.Count >= 5);

        var colors = runs.Select(r => r.Color).ToHashSet();
        Assert.Contains(RichTextCodec.RichTextColor.Red, colors);
        Assert.Contains(RichTextCodec.RichTextColor.Blue, colors);
        Assert.Contains(RichTextCodec.RichTextColor.Green, colors);
        Assert.Contains(RichTextCodec.RichTextColor.Gray, colors);
        Assert.Contains(runs, r => r.Bold);

        var again = RichTextCodec.Encode(plain, runs);
        var (plain2, runs2) = RichTextCodec.Parse(again);
        Assert.Equal(plain, plain2);
        Assert.Equal(runs.Count, runs2.Count);
    }
}
