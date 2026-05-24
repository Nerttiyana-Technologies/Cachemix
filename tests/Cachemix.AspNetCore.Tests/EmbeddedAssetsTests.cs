using Xunit;

namespace Cachemix.AspNetCore.Tests;

/// <summary>
/// Unit tests for <see cref="EmbeddedAssets"/>, the loader that serves the
/// dashboard's HTML, CSS and JavaScript from resources embedded in the assembly.
/// </summary>
public sealed class EmbeddedAssetsTests
{
    [Fact]
    public void Get_ReturnsTheIndexShell()
    {
        EmbeddedAsset? asset = EmbeddedAssets.Get("index.html");

        Assert.NotNull(asset);
        Assert.Equal("text/html; charset=utf-8", asset.ContentType);
        Assert.NotEmpty(asset.Content);
    }

    [Theory]
    [InlineData("cachemix.css", "text/css; charset=utf-8")]
    [InlineData("cachemix.js", "text/javascript; charset=utf-8")]
    public void Get_ReturnsEmbeddedAssetsWithTheExpectedContentType(string path, string expectedContentType)
    {
        EmbeddedAsset? asset = EmbeddedAssets.Get(path);

        Assert.NotNull(asset);
        Assert.Equal(expectedContentType, asset.ContentType);
        Assert.NotEmpty(asset.Content);
    }

    [Fact]
    public void Get_NormalizesALeadingSlash()
    {
        Assert.NotNull(EmbeddedAssets.Get("/index.html"));
    }

    [Fact]
    public void Get_ForAnUnknownAsset_ReturnsNull()
    {
        Assert.Null(EmbeddedAssets.Get("does-not-exist.js"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Get_ForAnEmptyPath_ReturnsNull(string path)
    {
        Assert.Null(EmbeddedAssets.Get(path));
    }

    [Theory]
    [InlineData("../index.html")]
    [InlineData("..\\index.html")]
    [InlineData("assets/../../secrets")]
    public void Get_ForAPathTraversalSequence_ReturnsNull(string path)
    {
        // Traversal sequences can never resolve a flat resource name, but they
        // are rejected outright as defence in depth.
        Assert.Null(EmbeddedAssets.Get(path));
    }
}
