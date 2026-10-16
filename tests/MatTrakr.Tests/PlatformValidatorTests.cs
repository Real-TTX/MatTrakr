using MatPlatform;
using MatTrakr.Platform;

namespace MatTrakr.Tests;

/// <summary>The definition of MatTrakr is valid for MatPlatform's validator, in both languages and against MatTrakr's copy of the MatUi sprite.</summary>
public sealed class PlatformValidatorTests(TrakrFactory site) : IClassFixture<TrakrFactory>
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void The_description_is_valid(string language)
        => Assert.Empty(MatPlatformValidator.Validate(MatPlatformBuilder.Build(TrakrPlatform.Describe, language), MatPlatformValidator.ReadSprite(Repo.Sprite)));

    [Fact]
    public async Task The_served_definition_is_valid()
        => Assert.Empty(MatPlatformValidator.Validate(await site.CreateClient().GetStringAsync("/api"), MatPlatformValidator.ReadSprite(Repo.Sprite)));

    [Fact]
    public void The_settings_are_few_and_the_roles_are_the_ones_of_the_app()
    {
        var definition = MatPlatformBuilder.Build(TrakrPlatform.Describe, "de");
        Assert.InRange(definition.Configure.Count, 1, 10);
        Assert.Equal(["admin", "verwalter", "user"], definition.Roles!.Select(r => r.Key));
        Assert.Equal("mattrakr", definition.App.Id);
        Assert.Equal("#6c5ce7", definition.App.Accent);
        Assert.True(File.Exists(Path.Combine(Repo.WebRoot, definition.App.Icon!.TrimStart('/'))));
    }
}
