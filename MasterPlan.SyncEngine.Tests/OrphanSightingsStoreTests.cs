using MasterPlan.SyncEngine;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class OrphanSightingsStoreTests
{
    [Fact]
    public void Save_then_load_roundtrips_ids()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mp-orphan-sightings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OrphanSightingsStore(dir);
            store.Save("ProjectHoursExtended", [3, 1, 2]);
            var loaded = store.Load("ProjectHoursExtended");
            Assert.Equal(new HashSet<int> { 1, 2, 3 }, loaded);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_missing_file_returns_empty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mp-orphan-sightings-" + Guid.NewGuid().ToString("N"));
        var store = new OrphanSightingsStore(dir);
        Assert.Empty(store.Load("ProjectHours"));
    }
}
