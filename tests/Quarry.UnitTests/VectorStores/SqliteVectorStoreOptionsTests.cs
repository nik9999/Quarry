using Quarry.Retrieval.VectorStores;
using Xunit;

namespace Quarry.Retrieval.Tests.VectorStores;

public class SqliteVectorStoreOptionsTests
{
    [Fact]
    public void DefaultOptions_HaveSensibleDefaults()
    {
        var options = new SqliteVectorStoreOptions();

        Assert.Equal("Data Source=quarry.db", options.ConnectionString);
        Assert.Equal("chunks", options.CollectionName);
        Assert.Equal(384, options.Dimensions);
        Assert.True(options.AutoEnsureCollection);
        Assert.Null(options.ExtensionPath);
    }

    [Theory]
    [InlineData("valid_name")]
    [InlineData("chunks123")]
    [InlineData("Table_A_1")]
    public void Constructor_ValidCollectionName_Succeeds(string collectionName)
    {
        var options = new SqliteVectorStoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            CollectionName = collectionName,
            Dimensions = 4
        };

        using var store = new SqliteVectorStore(options);

        Assert.Equal(collectionName, store.CollectionName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid name")]
    [InlineData("table-name")]
    [InlineData("table; DROP TABLE users;")]
    public void Constructor_InvalidCollectionName_ThrowsArgumentException(string invalidName)
    {
        var options = new SqliteVectorStoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            CollectionName = invalidName,
            Dimensions = 4
        };

        Assert.Throws<ArgumentException>(() => new SqliteVectorStore(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_NonPositiveDimensions_ThrowsArgumentOutOfRangeException(int dimensions)
    {
        var options = new SqliteVectorStoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            CollectionName = "test",
            Dimensions = dimensions
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new SqliteVectorStore(options));
    }
}
