using Quarry.Core.Filtering;
using Xunit;

namespace Quarry.Core.Tests.Filtering;

public class MetadataFilterTests
{
    [Fact]
    public void Matches_EmptyFilter_ReturnsTrueForNullMetadata()
    {
        var filter = new MetadataFilter();
        Assert.True(filter.Matches(null));
    }

    [Fact]
    public void Matches_EmptyFilter_ReturnsTrueForAnyMetadata()
    {
        var filter = new MetadataFilter();
        var metadata = new Dictionary<string, string> { ["author"] = "quarry" };
        Assert.True(filter.Matches(metadata));
    }

    [Fact]
    public void Matches_SingleCondition_MatchesCorrectly()
    {
        var filter = new MetadataFilter("category", "database");

        var matching = new Dictionary<string, string> { ["category"] = "database", ["tag"] = "sqlite" };
        var nonMatching = new Dictionary<string, string> { ["category"] = "frontend" };

        Assert.True(filter.Matches(matching));
        Assert.False(filter.Matches(nonMatching));
        Assert.False(filter.Matches(null));
    }

    [Fact]
    public void Matches_CaseInsensitiveKeysAndValues_Matches()
    {
        var filter = new MetadataFilter("Category", "DataBAse");
        var metadata = new Dictionary<string, string> { ["category"] = "database" };

        Assert.True(filter.Matches(metadata));
    }

    [Fact]
    public void Matches_MultipleConditions_RequiresAllToMatch()
    {
        var filter = new MetadataFilter
        {
            ExactMatches =
            {
                ["lang"] = "ru",
                ["level"] = "expert"
            }
        };

        var allMatch = new Dictionary<string, string> { ["lang"] = "ru", ["level"] = "expert" };
        var partialMatch = new Dictionary<string, string> { ["lang"] = "ru", ["level"] = "beginner" };
        var missingOne = new Dictionary<string, string> { ["lang"] = "ru" };

        Assert.True(filter.Matches(allMatch));
        Assert.False(filter.Matches(partialMatch));
        Assert.False(filter.Matches(missingOne));
    }
}
