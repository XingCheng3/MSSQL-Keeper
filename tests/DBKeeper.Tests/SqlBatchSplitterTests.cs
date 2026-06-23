using DBKeeper.Core.Helpers;

namespace DBKeeper.Tests;

public class SqlBatchSplitterTests
{
    [Fact]
    public void SplitBatches_ShouldSplitByStandaloneGo()
    {
        const string sql = """
            SELECT 1;
            GO
            SELECT 2;
            
            GO
            SELECT 3;
            """;

        var batches = SqlBatchSplitter.SplitBatches(sql);

        Assert.Equal(3, batches.Count);
        Assert.Equal("SELECT 1;", batches[0]);
        Assert.Equal("SELECT 2;", batches[1]);
        Assert.Equal("SELECT 3;", batches[2]);
    }
}
