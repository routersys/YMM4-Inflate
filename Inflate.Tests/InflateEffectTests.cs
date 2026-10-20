using System.ComponentModel.DataAnnotations;
using System.Reflection;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;

namespace Inflate.Tests;

public sealed class InflateEffectTests
{
    static PropertyInfo Property(string name) => typeof(InflateEffect).GetProperty(name)!;

    static T Attribute<T>(string property) where T : Attribute => Property(property).GetCustomAttribute<T>()!;

    static Animation[] Animations(InflateEffect effect)
        => [effect.Amount, effect.Height, effect.LightDirection, effect.LightElevation, effect.Ambient, effect.Specular, effect.Sharpness, effect.Reflection, effect.Rim, effect.ColorKeep];

    [Theory]
    [InlineData(nameof(InflateEffect.Amount), 100d, 0d, 100d)]
    [InlineData(nameof(InflateEffect.Height), 100d, 0d, 300d)]
    [InlineData(nameof(InflateEffect.LightDirection), -45d, -180d, 180d)]
    [InlineData(nameof(InflateEffect.LightElevation), 50d, 0d, 90d)]
    [InlineData(nameof(InflateEffect.Ambient), 30d, 0d, 100d)]
    [InlineData(nameof(InflateEffect.Specular), 80d, 0d, 200d)]
    [InlineData(nameof(InflateEffect.Sharpness), 50d, 0d, 100d)]
    [InlineData(nameof(InflateEffect.Reflection), 30d, 0d, 100d)]
    [InlineData(nameof(InflateEffect.Rim), 20d, 0d, 100d)]
    [InlineData(nameof(InflateEffect.ColorKeep), 100d, 0d, 100d)]
    public void AnimatedParametersStartFromTheirDefaultsWithinTheirRange(string name, double defaultValue, double minimum, double maximum)
    {
        var effect = new InflateEffect();

        var animation = (Animation)Property(name).GetValue(effect)!;

        Assert.Equal(defaultValue, animation.DefaultValue);
        Assert.Equal(minimum, animation.MinValue);
        Assert.Equal(maximum, animation.MaxValue);
        Assert.Equal(defaultValue, animation.GetValue(0, 1, EffectDescriptions.Fps));
    }

    [Fact]
    public void TheQualityStartsFromHigh()
    {
        var effect = new InflateEffect();

        Assert.Equal(InflateQuality.High, effect.Quality);
    }

    [Fact]
    public void ChangingTheQualityNotifiesTheEditor()
    {
        var effect = new InflateEffect();
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.Quality = InflateQuality.Ultra;

        Assert.Equal([nameof(InflateEffect.Quality)], changed);
    }

    [Fact]
    public void AssigningAnUnchangedQualityDoesNotNotify()
    {
        var effect = new InflateEffect();
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.Quality = InflateQuality.High;

        Assert.Empty(changed);
    }

    [Fact]
    public void TheAnimatedParametersAreListedOnceAndReused()
    {
        var effect = new InflateEffect();
        var method = typeof(InflateEffect).GetMethod("GetAnimatables", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var first = (IEnumerable<IAnimatable>)method.Invoke(effect, null)!;
        var second = (IEnumerable<IAnimatable>)method.Invoke(effect, null)!;

        Assert.Same(first, second);
        Assert.Equal(Animations(effect), first);
    }

    [Fact]
    public void TheLabelIsTheLocalizedEffectName()
    {
        var effect = new InflateEffect();

        Assert.Equal(Texts.Inflate, effect.Label);
    }

    [Fact]
    public void TheTenNumericParametersReceiveTheAnimationParameters()
    {
        var effect = new InflateEffect();

        effect.SetAnimationParameters(120, EffectDescriptions.Fps);

        Assert.Equal(10, Animations(effect).Length);
        Assert.All(Animations(effect), animation => Assert.Equal(120, animation.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NoExoFilterIsWrittenForAviUtl(int keyFrameIndex)
    {
        var effect = new InflateEffect();

        var description = new ExoOutputDescription(new VideoInfo(), string.Empty, new AviUtlDirectories(string.Empty, string.Empty));

        Assert.Empty(effect.CreateExoVideoFilters(keyFrameIndex, description));
    }

    [Fact]
    public void TheAuthorIsDeclared()
    {
        var details = typeof(InflateEffect).GetCustomAttribute<PluginDetailsAttribute>()!;

        Assert.Equal("routersys", details.AuthorName);
    }

    [Fact]
    public void TheEffectIsRegisteredForFilteringAndDecorationWithoutAviUtlSupport()
    {
        var attribute = typeof(InflateEffect).GetCustomAttribute<VideoEffectAttribute>()!;

        Assert.Equal(nameof(Texts.Inflate), attribute.Name);
        Assert.Equal([VideoEffectCategories.Filtering, VideoEffectCategories.Decoration], attribute.Categories);
        Assert.Equal([nameof(Texts.TagInflate), nameof(Texts.TagThreeDimensional), nameof(Texts.TagGloss)], attribute.Keywords);
        Assert.False(attribute.IsAviUtlSupported);
        Assert.True(attribute.IsEffectItemSupported);
        Assert.Equal(typeof(Texts), attribute.ResourceType);
        Assert.Equal(Texts.Inflate, attribute.GetName());
    }

    [Theory]
    [InlineData(nameof(InflateEffect.Amount), nameof(Texts.BasicGroup), nameof(Texts.Amount), nameof(Texts.AmountDescription), 0)]
    [InlineData(nameof(InflateEffect.Height), nameof(Texts.BasicGroup), nameof(Texts.Height), nameof(Texts.HeightDescription), 1)]
    [InlineData(nameof(InflateEffect.Quality), nameof(Texts.BasicGroup), nameof(Texts.Quality), nameof(Texts.QualityDescription), 2)]
    [InlineData(nameof(InflateEffect.LightDirection), nameof(Texts.LightGroup), nameof(Texts.LightDirection), nameof(Texts.LightDirectionDescription), 10)]
    [InlineData(nameof(InflateEffect.LightElevation), nameof(Texts.LightGroup), nameof(Texts.LightElevation), nameof(Texts.LightElevationDescription), 11)]
    [InlineData(nameof(InflateEffect.Ambient), nameof(Texts.LightGroup), nameof(Texts.Ambient), nameof(Texts.AmbientDescription), 12)]
    [InlineData(nameof(InflateEffect.Specular), nameof(Texts.MaterialGroup), nameof(Texts.Specular), nameof(Texts.SpecularDescription), 20)]
    [InlineData(nameof(InflateEffect.Sharpness), nameof(Texts.MaterialGroup), nameof(Texts.Sharpness), nameof(Texts.SharpnessDescription), 21)]
    [InlineData(nameof(InflateEffect.Reflection), nameof(Texts.MaterialGroup), nameof(Texts.Reflection), nameof(Texts.ReflectionDescription), 22)]
    [InlineData(nameof(InflateEffect.Rim), nameof(Texts.MaterialGroup), nameof(Texts.Rim), nameof(Texts.RimDescription), 23)]
    [InlineData(nameof(InflateEffect.ColorKeep), nameof(Texts.MaterialGroup), nameof(Texts.ColorKeep), nameof(Texts.ColorKeepDescription), 24)]
    public void EveryParameterIsDisplayedInItsGroupInOrder(string property, string group, string name, string description, int order)
    {
        var display = Attribute<DisplayAttribute>(property);

        Assert.Equal(group, display.GroupName);
        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(order, display.Order);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Theory]
    [InlineData(nameof(InflateEffect.Amount), "%", 0d, 100d)]
    [InlineData(nameof(InflateEffect.Height), "%", 0d, 300d)]
    [InlineData(nameof(InflateEffect.LightDirection), "°", -180d, 180d)]
    [InlineData(nameof(InflateEffect.LightElevation), "°", 0d, 90d)]
    [InlineData(nameof(InflateEffect.Ambient), "%", 0d, 100d)]
    [InlineData(nameof(InflateEffect.Specular), "%", 0d, 200d)]
    [InlineData(nameof(InflateEffect.Sharpness), "%", 0d, 100d)]
    [InlineData(nameof(InflateEffect.Reflection), "%", 0d, 100d)]
    [InlineData(nameof(InflateEffect.Rim), "%", 0d, 100d)]
    [InlineData(nameof(InflateEffect.ColorKeep), "%", 0d, 100d)]
    public void AnimatedParametersAreEditedWithAnimationSliders(string property, string unit, double minimum, double maximum)
    {
        var slider = Attribute<AnimationSliderAttribute>(property);

        Assert.Equal("F1", slider.StringFormat);
        Assert.Equal(unit, slider.UnitText);
        Assert.Equal(minimum, slider.DefaultMin);
        Assert.Equal(maximum, slider.DefaultMax);
    }

    [Fact]
    public void TheQualityIsChosenFromACombo()
    {
        Assert.NotNull(Attribute<EnumComboBoxAttribute>(nameof(InflateEffect.Quality)));
    }

    [Fact]
    public void TheQualitiesAreListedInOrder()
    {
        Assert.Equal([InflateQuality.Balanced, InflateQuality.High, InflateQuality.Ultra], Enum.GetValues<InflateQuality>());
        Assert.Equal([0, 1, 2], Enum.GetValues<InflateQuality>().Select(quality => (int)quality));
    }

    [Theory]
    [InlineData(InflateQuality.Balanced, nameof(Texts.QualityBalanced), nameof(Texts.QualityBalancedDescription))]
    [InlineData(InflateQuality.High, nameof(Texts.QualityHigh), nameof(Texts.QualityHighDescription))]
    [InlineData(InflateQuality.Ultra, nameof(Texts.QualityUltra), nameof(Texts.QualityUltraDescription))]
    public void EveryQualityIsDisplayedWithItsLocalizedName(InflateQuality quality, string name, string description)
    {
        var display = typeof(InflateQuality).GetField(quality.ToString())!.GetCustomAttribute<DisplayAttribute>()!;

        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Fact]
    public void EverySettingSurvivesAProjectRoundTrip()
    {
        var effect = new InflateEffect { Quality = InflateQuality.Ultra };
        var values = new[] { 55d, 140d, 70d, 20d, 65d, 150d, 33d, 44d, 77d, 88d };
        foreach (var (animation, value) in Animations(effect).Zip(values))
            animation.Values[0].Value = value;

        var clone = Json.GetClone(effect)!;

        Assert.NotSame(effect, clone);
        Assert.Equal(InflateQuality.Ultra, clone.Quality);
        Assert.Equal(values, Animations(clone).Select(animation => animation.GetValue(0, 1, EffectDescriptions.Fps)));
    }
}
