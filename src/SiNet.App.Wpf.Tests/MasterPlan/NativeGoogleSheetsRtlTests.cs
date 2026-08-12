using Google.Apis.Sheets.v4.Data;
using SiNet.Infrastructure.Google.Reports;
using Xunit;

namespace SiNet.App.Wpf.Tests.MasterPlan;

public sealed class NativeGoogleSheetsRtlTests
{
    [Fact]
    public void CreateRightToLeftRequest_uses_numeric_sheetId_and_rightToLeft_field()
    {
        const int sheetId = 42_017;
        var request = NativeGoogleSheetsWriter.CreateRightToLeftRequest(sheetId);

        Assert.NotNull(request.UpdateSheetProperties);
        Assert.Null(request.AddSheet);
        Assert.Equal(sheetId, request.UpdateSheetProperties.Properties.SheetId);
        Assert.True(request.UpdateSheetProperties.Properties.RightToLeft);
        Assert.Equal("rightToLeft", request.UpdateSheetProperties.Fields);
        Assert.Null(request.UpdateSheetProperties.Properties.Title);
    }

    [Fact]
    public void CreateRightToLeftRequestsForSheets_one_request_per_sheetId()
    {
        var sheets = new List<Sheet>
        {
            new() { Properties = new SheetProperties { SheetId = 10, Title = "Data" } },
            new() { Properties = new SheetProperties { SheetId = 20, Title = "סיכום" } },
            new() { Properties = new SheetProperties { SheetId = null, Title = "broken" } },
        };

        var requests = NativeGoogleSheetsWriter.CreateRightToLeftRequestsForSheets(sheets);

        Assert.Equal(2, requests.Count);
        Assert.Equal(10, requests[0].UpdateSheetProperties!.Properties.SheetId);
        Assert.Equal(20, requests[1].UpdateSheetProperties!.Properties.SheetId);
        Assert.All(requests, r => Assert.True(r.UpdateSheetProperties!.Properties.RightToLeft));
        Assert.All(requests, r => Assert.Equal("rightToLeft", r.UpdateSheetProperties!.Fields));
    }

    [Fact]
    public void CreateRightToLeftRequest_does_not_use_sheet_title_as_id()
    {
        var request = NativeGoogleSheetsWriter.CreateRightToLeftRequest(7);
        Assert.IsType<int>(request.UpdateSheetProperties!.Properties.SheetId);
        Assert.DoesNotContain(
            "Data",
            request.UpdateSheetProperties.Properties.SheetId?.ToString() ?? string.Empty,
            StringComparison.Ordinal);
    }
}
