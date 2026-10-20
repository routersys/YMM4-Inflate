using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;

namespace Inflate;

[PluginDetails(AuthorName = "routersys")]
[VideoEffect(nameof(Texts.Inflate), [VideoEffectCategories.Filtering, VideoEffectCategories.Decoration], [nameof(Texts.TagInflate), nameof(Texts.TagThreeDimensional), nameof(Texts.TagGloss)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class InflateEffect : VideoEffectBase
{
    public override string Label => Texts.Inflate;

    public InflateEffect()
    {
        InflateTelemetry.EnsureStartedOnce();
        InflateUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Amount), Description = nameof(Texts.AmountDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Height), Description = nameof(Texts.HeightDescription), Order = 1, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 300)]
    public Animation Height { get; } = new Animation(100, 0, 300);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Quality), Description = nameof(Texts.QualityDescription), Order = 2, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public InflateQuality Quality { get => _quality; set => Set(ref _quality, value); }
    private InflateQuality _quality = InflateQuality.High;

    [Display(GroupName = nameof(Texts.LightGroup), Name = nameof(Texts.LightDirection), Description = nameof(Texts.LightDirectionDescription), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", -180, 180)]
    public Animation LightDirection { get; } = new Animation(-45, -180, 180);

    [Display(GroupName = nameof(Texts.LightGroup), Name = nameof(Texts.LightElevation), Description = nameof(Texts.LightElevationDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", 0, 90)]
    public Animation LightElevation { get; } = new Animation(50, 0, 90);

    [Display(GroupName = nameof(Texts.LightGroup), Name = nameof(Texts.Ambient), Description = nameof(Texts.AmbientDescription), Order = 12, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Ambient { get; } = new Animation(30, 0, 100);

    [Display(GroupName = nameof(Texts.MaterialGroup), Name = nameof(Texts.Specular), Description = nameof(Texts.SpecularDescription), Order = 20, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 200)]
    public Animation Specular { get; } = new Animation(80, 0, 200);

    [Display(GroupName = nameof(Texts.MaterialGroup), Name = nameof(Texts.Sharpness), Description = nameof(Texts.SharpnessDescription), Order = 21, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Sharpness { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.MaterialGroup), Name = nameof(Texts.Reflection), Description = nameof(Texts.ReflectionDescription), Order = 22, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Reflection { get; } = new Animation(30, 0, 100);

    [Display(GroupName = nameof(Texts.MaterialGroup), Name = nameof(Texts.Rim), Description = nameof(Texts.RimDescription), Order = 23, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Rim { get; } = new Animation(20, 0, 100);

    [Display(GroupName = nameof(Texts.MaterialGroup), Name = nameof(Texts.ColorKeep), Description = nameof(Texts.ColorKeepDescription), Order = 24, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation ColorKeep { get; } = new Animation(100, 0, 100);

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
    {
        try
        {
            return new InflateEffectProcessor(devices, this);
        }
        catch (Exception exception)
        {
            InflateTelemetry.Report(exception);
            throw;
        }
    }

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Amount, Height, LightDirection, LightElevation, Ambient, Specular, Sharpness, Reflection, Rim, ColorKeep];
}
