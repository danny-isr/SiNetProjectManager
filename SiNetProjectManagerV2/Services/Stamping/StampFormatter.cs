using System;
using System.Collections.Generic;

namespace SiNetProjectManagerV2.Services.Stamping;

/// <summary>
/// Utility for preparing fixed-length replacement texts for DWF stamp X-placeholders.
/// </summary>
public static class StampFormatter
{
    /// <summary>
    /// Given the X-placeholder pattern (e.g. 47 X's) and user sentences (one per line),
    /// produces a list of fixed-length replacement strings — one per occurrence found in the DWF.
    /// Each replacement has the exact same character count as <paramref name="xPattern"/>.
    /// </summary>
    public static IReadOnlyList<string> BuildSequentialReplacements(
        string xPattern,
        int occurrenceCount,
        IList<string>? sentences)
    {
        sentences ??= Array.Empty<string>();
        var result = new List<string>(occurrenceCount);

        for (int i = 0; i < occurrenceCount; i++)
        {
            var sentence = i < sentences.Count
                ? (sentences[i] ?? string.Empty)
                : string.Empty;

            result.Add(PrepareFixedLengthText(sentence, xPattern.Length));
        }

        return result;
    }

    /// <summary>
    /// Pads or trims <paramref name="text"/> to exactly <paramref name="exactLength"/> characters.
    /// Newlines are replaced with spaces.
    /// </summary>
    public static string PrepareFixedLengthText(string text, int exactLength)
    {
        if (string.IsNullOrEmpty(text))
            return new string(' ', exactLength);

        text = text.Replace("\r", " ").Replace("\n", " ");

        if (text.Length > exactLength)
            return text[..exactLength];

        if (text.Length < exactLength)
            return text.PadRight(exactLength, ' ');

        return text;
    }
}
