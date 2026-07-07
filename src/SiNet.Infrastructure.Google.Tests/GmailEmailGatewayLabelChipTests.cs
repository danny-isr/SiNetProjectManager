using Google.Apis.Gmail.v1.Data;
using SiNet.Application.Abstractions.Email;
using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

public sealed class GmailEmailGatewayLabelChipTests
{
    [Fact]
    public void ResolveLabelChips_maps_gmail_label_colors()
    {
        var message = new Message
        {
            LabelIds = ["Label_1", "Label_2"],
        };

        var labelMap = new Dictionary<string, Label>
        {
            ["Label_1"] = new()
            {
                Id = "Label_1",
                Name = "OfficeSystem_Pending",
                Color = new LabelColor
                {
                    BackgroundColor = "#F3E5F5",
                    TextColor = "#4A148C",
                },
            },
            ["Label_2"] = new()
            {
                Id = "Label_2",
                Name = "INBOX",
            },
        };

        var chips = GmailEmailGateway.ResolveLabelChips(message, labelMap);

        Assert.Equal(2, chips.Count);
        var pending = Assert.Single(chips, chip => chip.DisplayName == "OfficeSystem_Pending");
        Assert.Equal("#F3E5F5", pending.BackgroundColor);
        Assert.Equal("#4A148C", pending.TextColor);
    }

    [Fact]
    public void ResolveLabelChips_returns_empty_when_label_map_missing()
    {
        var message = new Message { LabelIds = ["Label_1"] };

        var chips = GmailEmailGateway.ResolveLabelChips(message, labelMap: null);

        Assert.Empty(chips);
    }
}
