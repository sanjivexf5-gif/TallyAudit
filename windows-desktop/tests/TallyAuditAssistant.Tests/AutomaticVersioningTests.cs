using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class AutomaticVersioningTests
{
    [Theory]
    [InlineData("1.0.16", "1.0.17")]
    [InlineData("1.0.17", "1.0.18")]
    [InlineData("1.2.9", "1.2.10")]
    [InlineData("2.9.9", "2.9.10")]
    public void Version_Increment_CalculatesCorrectNextPatchVersion(string current, string expected)
    {
        var currentVer = Version.Parse(current);
        var nextVer = new Version(currentVer.Major, currentVer.Minor, currentVer.Build + 1);
        Assert.Equal(expected, nextVer.ToString(3));
    }

    [Fact]
    public void Version_Filter_IgnoresNonSemanticTags()
    {
        var rawTags = new[] { "v1.0.16", "v1.0.15", "beta-v1.0.0", "v1.0-alpha", "latest", "v2.0" };
        var validVersions = new List<Version>();

        foreach (var tag in rawTags)
        {
            var cleanTag = tag.StartsWith("v") ? tag.Substring(1) : tag;
            if (Version.TryParse(cleanTag, out var parsed) && cleanTag.Split('.').Length == 3)
            {
                validVersions.Add(parsed);
            }
        }

        Assert.Equal(2, validVersions.Count);
        Assert.Contains(new Version(1, 0, 16), validVersions);
        Assert.Contains(new Version(1, 0, 15), validVersions);
    }

    [Theory]
    [InlineData("1.0.17", "1.0.16", true)]
    [InlineData("1.0.16", "1.0.16", false)]
    [InlineData("1.0.15", "1.0.16", false)]
    [InlineData("1.1.0", "1.0.16", true)]
    public void Version_Comparison_DeterminesNewerVersionCorrectly(string latest, string installed, bool isNewer)
    {
        var latestVer = Version.Parse(latest);
        var installedVer = Version.Parse(installed);
        var result = latestVer > installedVer;
        Assert.Equal(isNewer, result);
    }
}
