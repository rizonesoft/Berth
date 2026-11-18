namespace Berth.Tests;

public sealed class TestDataTests
{
    [Fact]
    public void TwoLiveInstancesAreSeparateAndEachDisposeRemovesOnlyItsOwn()
    {
        string? before = Environment.GetEnvironmentVariable("BERTH_DATA");
        var first = new TestData();
        var second = new TestData();
        try
        {
            File.WriteAllText(Path.Combine(first.Root, "marker"), "1");
            File.WriteAllText(Path.Combine(second.Root, "marker"), "2");

            Assert.NotEqual(first.Root, second.Root, StringComparer.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(first.Root, "marker")));
            Assert.True(File.Exists(Path.Combine(second.Root, "marker")));
            Assert.Equal(before, Environment.GetEnvironmentVariable("BERTH_DATA"));

            first.Dispose();
            Assert.False(Directory.Exists(first.Root));
            Assert.True(File.Exists(Path.Combine(second.Root, "marker")));

            second.Dispose();
            Assert.False(Directory.Exists(second.Root));
            Assert.Equal(before, Environment.GetEnvironmentVariable("BERTH_DATA"));
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }
}
