using System.Globalization;
using System.Numerics;
using ComputeWeave;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Player.Video;

namespace Inflate.Tests;

[Collection("Direct2D")]
public sealed class InflateEffectProcessorTests
{
    const int Size = 64;
    const int Length = 30;

    static readonly Bgra Gray = Bgra.Opaque(192, 192, 192);

    static readonly Bgra Amber = Bgra.Opaque(80, 200, 255);

    static readonly Bgra Azure = Bgra.Opaque(255, 120, 60);

    static Bgra Scene(int x, int y)
    {
        var dx = x - 22;
        var dy = y - 30;
        if (dx * dx + dy * dy <= 144)
            return Amber;
        return x is >= 40 and < 56 && y is >= 36 and < 52 ? Azure : Bgra.Transparent;
    }

    static Func<int, int, Bgra> Filled(Bgra color) => (x, y) => x is >= 16 and < 48 && y is >= 16 and < 48 ? color : Bgra.Transparent;

    static void RequireInterop(IGraphicsDevicesAndContext devices)
    {
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = InflateInteropProvider.TryCreate(devices, scheduler, out var device);
        if (provider is null || device is null)
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
    }

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}"""))!;

    static Rendering RenderFrame(IGraphicsDevicesAndContext devices, IVideoEffectProcessor processor, int frame)
    {
        processor.Update(EffectDescriptions.At(frame, Length));
        return Rendering.Capture(devices, processor.Output);
    }

    static Rendering RenderFresh(IGraphicsDevicesAndContext devices, InflateEffect effect, SourceImage source, int frame = 0)
    {
        using var processor = effect.CreateVideoEffect(devices);
        processor.SetInput(source.Bitmap);
        return RenderFrame(devices, processor, frame);
    }

    static void AssertSameAsSource(IGraphicsDevicesAndContext devices, Rendering rendering, SourceImage source, string? setting = null)
    {
        var expected = Rendering.Capture(devices, source.Bitmap);

        Assert.Equal((0, 0, source.Width, source.Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.True(rendering.SamePixelsAs(expected), setting);
    }

    static int[] Pixels(SourceImage source)
    {
        var pixels = new int[source.Width * source.Height];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source[x, y];
                pixels[y * source.Width + x] = pixel.Alpha << 24 | pixel.Red << 16 | pixel.Green << 8 | pixel.Blue;
            }
        }

        return pixels;
    }

    static int[] ThroughThePipeline(SourceImage source, InflatePipeline.Parameters parameters)
    {
        using var pipeline = InflatePipeline.TryCreate();
        Assert.NotNull(pipeline);
        var destination = new int[source.Width * source.Height];
        pipeline.Process(Pixels(source), destination, source.Width, source.Height, in parameters);
        return destination;
    }

    static bool Close(Bgra pixel, int expected)
        => Math.Abs(pixel.Alpha - ((expected >> 24) & 255)) <= 1
            && Math.Abs(pixel.Red - ((expected >> 16) & 255)) <= 1
            && Math.Abs(pixel.Green - ((expected >> 8) & 255)) <= 1
            && Math.Abs(pixel.Blue - (expected & 255)) <= 1;

    static bool Differs(Rendering rendering, SourceImage source)
        => rendering.Coordinates().Any(point => rendering[point.X, point.Y] != source[point.X, point.Y]);

    [Fact]
    public void TheProcessorHandsTheDrawDescriptionBackUnchanged()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, Scene);
        using var processor = new InflateEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var description = EffectDescriptions.At(0, Length);

        var draw = processor.Update(description);

        Assert.Same(description.DrawDescription, draw);
    }

    public static readonly TheoryData<string, Action<InflateEffect>, InflateQuality, float[]> Settings = new()
    {
        { "Default", _ => { }, InflateQuality.High, [1f, -45f, 50f, 0.3f, 0.8f, 0.5f, 0.3f, 0.2f, 1f] },
        { nameof(InflateEffect.Height), effect => effect.Height.Values[0].Value = 250d, InflateQuality.High, [2.5f, -45f, 50f, 0.3f, 0.8f, 0.5f, 0.3f, 0.2f, 1f] },
        { nameof(InflateEffect.Quality), effect => effect.Quality = InflateQuality.Balanced, InflateQuality.Balanced, [1f, -45f, 50f, 0.3f, 0.8f, 0.5f, 0.3f, 0.2f, 1f] },
        { nameof(InflateEffect.LightDirection), effect => effect.LightDirection.Values[0].Value = 120d, InflateQuality.High, [1f, 120f, 50f, 0.3f, 0.8f, 0.5f, 0.3f, 0.2f, 1f] },
        { nameof(InflateEffect.LightElevation), effect => effect.LightElevation.Values[0].Value = 15d, InflateQuality.High, [1f, -45f, 15f, 0.3f, 0.8f, 0.5f, 0.3f, 0.2f, 1f] },
        { nameof(InflateEffect.Ambient), effect => effect.Ambient.Values[0].Value = 70d, InflateQuality.High, [1f, -45f, 50f, 0.7f, 0.8f, 0.5f, 0.3f, 0.2f, 1f] },
        { nameof(InflateEffect.Specular), effect => effect.Specular.Values[0].Value = 180d, InflateQuality.High, [1f, -45f, 50f, 0.3f, 1.8f, 0.5f, 0.3f, 0.2f, 1f] },
        { nameof(InflateEffect.Sharpness), effect => effect.Sharpness.Values[0].Value = 90d, InflateQuality.High, [1f, -45f, 50f, 0.3f, 0.8f, 0.9f, 0.3f, 0.2f, 1f] },
        { nameof(InflateEffect.Reflection), effect => effect.Reflection.Values[0].Value = 90d, InflateQuality.High, [1f, -45f, 50f, 0.3f, 0.8f, 0.5f, 0.9f, 0.2f, 1f] },
        { nameof(InflateEffect.Rim), effect => effect.Rim.Values[0].Value = 80d, InflateQuality.High, [1f, -45f, 50f, 0.3f, 0.8f, 0.5f, 0.3f, 0.8f, 1f] },
        { nameof(InflateEffect.ColorKeep), effect => effect.ColorKeep.Values[0].Value = 40d, InflateQuality.High, [1f, -45f, 50f, 0.3f, 0.8f, 0.5f, 0.3f, 0.2f, 0.4f] },
    };

    [Theory]
    [MemberData(nameof(Settings))]
    public void EverySettingReachesThePipeline(string setting, Action<InflateEffect> configure, InflateQuality quality, float[] values)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Scene);
        var effect = new InflateEffect();
        configure(effect);
        var expected = ThroughThePipeline(source, new InflatePipeline.Parameters(quality, values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8]));

        var rendering = RenderFresh(context, effect, source);

        Assert.Equal((0, 0, Size, Size), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.True(Close(rendering[point.X, point.Y], expected[point.Y * Size + point.X]), $"{setting} ({point.X}, {point.Y})"));
        Assert.True(Differs(rendering, source), setting);
    }

    [Fact]
    public void TheShapeOfTheSourceIsKeptAndTheColorStaysPremultiplied()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Scene);

        var rendering = RenderFresh(context, new InflateEffect(), source);

        Assert.All(rendering.Coordinates(), point =>
        {
            var pixel = rendering[point.X, point.Y];
            Assert.Equal(source[point.X, point.Y].Alpha, pixel.Alpha);
            Assert.InRange(pixel.Blue, 0, pixel.Alpha);
            Assert.InRange(pixel.Green, 0, pixel.Alpha);
            Assert.InRange(pixel.Red, 0, pixel.Alpha);
        });
    }

    [Fact]
    public void AZeroAmountPassesTheImageThrough()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, Scene);
        var effect = new InflateEffect();
        effect.Amount.Values[0].Value = 0d;

        var rendering = RenderFresh(context, effect, source);

        AssertSameAsSource(context, rendering, source);
    }

    [Theory]
    [InlineData(8193, 2)]
    [InlineData(2, 8193)]
    public void AnImageBeyondTheSupportedSizePassesThroughUntouched(int width, int height)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, width, height, (x, y) => x < width / 2 ? Gray : Bgra.Transparent);

        var rendering = RenderFresh(context, new InflateEffect(), source);

        AssertSameAsSource(context, rendering, source);
    }

    [Theory]
    [InlineData(8192, 8)]
    [InlineData(8, 8192)]
    public void AnImageAtTheLongestSupportedSideIsStillInflated(int width, int height)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, width, height, (x, y) => x < 6 || y < 6 || x > width - 7 || y > height - 7 ? Gray : Bgra.Transparent);

        var rendering = RenderFresh(context, new InflateEffect(), source);

        Assert.Equal((0, 0, width, height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.False(rendering.SamePixelsAs(Rendering.Capture(context, source.Bitmap)));
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(-37, 21)]
    public void TheInflationTravelsWithTheImage(int dx, int dy)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Scene);
        using var moved = new AffineTransform2D(context.DeviceContext)
        {
            InterPolationMode = AffineTransform2DInterpolationMode.NearestNeighbor,
            BorderMode = BorderMode.Hard,
            TransformMatrix = Matrix3x2.CreateTranslation(dx, dy),
        };
        moved.SetInput(0, source.Bitmap, true);
        using var movedOutput = moved.Output;
        using var inPlaceProcessor = new InflateEffect().CreateVideoEffect(context);
        inPlaceProcessor.SetInput(source.Bitmap);
        var inPlace = RenderFrame(context, inPlaceProcessor, 0);
        using var travelledProcessor = new InflateEffect().CreateVideoEffect(context);
        travelledProcessor.SetInput(movedOutput);

        var travelled = RenderFrame(context, travelledProcessor, 0);

        Assert.Equal((inPlace.Left + dx, inPlace.Top + dy, inPlace.Width, inPlace.Height), (travelled.Left, travelled.Top, travelled.Width, travelled.Height));
        Assert.All(inPlace.Coordinates(), point => Assert.True(inPlace[point.X, point.Y] == travelled[point.X + dx, point.Y + dy], $"({point.X}, {point.Y})"));
        Assert.True(Differs(inPlace, source));
    }

    [Fact]
    public void ReturningToAFrameReproducesItExactly()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Scene);
        var effect = new InflateEffect();
        effect.Height.CopyFrom(Linear(10d, 250d));
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var first = RenderFrame(context, processor, 4);
        var other = RenderFrame(context, processor, 20);
        var again = RenderFrame(context, processor, 4);

        Assert.False(first.SamePixelsAs(other));
        Assert.True(first.SamePixelsAs(again));
    }

    [Fact]
    public void TheNextFrameOfAnUnchangedSettingIsDrawnTheSame()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Scene);
        using var processor = new InflateEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var first = RenderFrame(context, processor, 0);
        var next = RenderFrame(context, processor, 1);

        Assert.True(Differs(first, source));
        Assert.True(first.SamePixelsAs(next));
    }

    [Fact]
    public void ANewProcessorDrawsTheSameImage()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Scene);
        var effect = new InflateEffect();

        var first = RenderFresh(context, effect, source);
        var second = RenderFresh(context, effect, source);

        Assert.True(first.SamePixelsAs(second));
    }

    [Fact]
    public void AnimatedAmountIsReadAtEachFrame()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Scene);
        var effect = new InflateEffect();
        effect.Amount.CopyFrom(Linear(0d, 100d));
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var start = RenderFrame(context, processor, 0);
        var end = RenderFrame(context, processor, Length - 1);
        var back = RenderFrame(context, processor, 0);

        AssertSameAsSource(context, start, source);
        Assert.True(Differs(end, source));
        AssertSameAsSource(context, back, source);
    }

    [Theory]
    [InlineData(100d, 30d)]
    [InlineData(30d, 100d)]
    [InlineData(0d, 200d)]
    public void AProcessorThatDrewAnotherHeightDrawsLikeAFreshOne(double firstHeight, double secondHeight)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Scene);
        var effect = new InflateEffect();
        effect.Height.Values[0].Value = firstHeight;
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        RenderFrame(context, processor, 0);
        effect.Height.Values[0].Value = secondHeight;
        var expected = RenderFresh(context, effect, source);

        var reused = RenderFrame(context, processor, 0);

        Assert.True(reused.SamePixelsAs(expected));
    }

    public static readonly TheoryData<string, Action<InflateEffect>> LaterChanges = new()
    {
        { nameof(InflateEffect.Amount), effect => effect.Amount.Values[0].Value = 50d },
        { nameof(InflateEffect.Height), effect => effect.Height.Values[0].Value = 250d },
        { nameof(InflateEffect.Quality), effect => effect.Quality = InflateQuality.Balanced },
        { nameof(InflateEffect.LightDirection), effect => effect.LightDirection.Values[0].Value = 120d },
        { nameof(InflateEffect.LightElevation), effect => effect.LightElevation.Values[0].Value = 15d },
        { nameof(InflateEffect.Ambient), effect => effect.Ambient.Values[0].Value = 70d },
        { nameof(InflateEffect.Specular), effect => effect.Specular.Values[0].Value = 180d },
        { nameof(InflateEffect.Sharpness), effect => effect.Sharpness.Values[0].Value = 90d },
        { nameof(InflateEffect.Reflection), effect => effect.Reflection.Values[0].Value = 90d },
        { nameof(InflateEffect.Rim), effect => effect.Rim.Values[0].Value = 80d },
        { nameof(InflateEffect.ColorKeep), effect => effect.ColorKeep.Values[0].Value = 40d },
    };

    [Theory]
    [MemberData(nameof(LaterChanges))]
    public void EverySettingChangedAfterTheFirstFrameReachesTheEffect(string setting, Action<InflateEffect> change)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Scene);
        var effect = new InflateEffect();
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var before = RenderFrame(context, processor, 0);
        change(effect);
        var after = RenderFrame(context, processor, 0);
        var expected = RenderFresh(context, effect, source);

        Assert.False(before.SamePixelsAs(after), setting);
        Assert.True(after.SamePixelsAs(expected), setting);
    }

    [Theory]
    [InlineData(32, 128)]
    [InlineData(128, 32)]
    [InlineData(64, 48)]
    public void AResizedSourceIsDrawnLikeAFreshOne(int firstSize, int secondSize)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var first = new SourceImage(context, firstSize, firstSize, (x, y) => (x - 8) * (x - 8) + (y - 8) * (y - 8) <= 36 ? Amber : Bgra.Transparent);
        using var second = new SourceImage(context, secondSize, secondSize, (x, y) => (x - 20) * (x - 20) + (y - 12) * (y - 12) <= 64 ? Azure : Bgra.Transparent);
        var effect = new InflateEffect();
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(first.Bitmap);
        RenderFrame(context, processor, 0);
        var expected = RenderFresh(context, effect, second);

        processor.SetInput(second.Bitmap);
        var after = RenderFrame(context, processor, 0);

        Assert.Equal((0, 0, secondSize, secondSize), (after.Left, after.Top, after.Width, after.Height));
        Assert.True(Differs(after, second));
        Assert.True(after.SamePixelsAs(expected));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InflationAppearsOnceATransparentSourceTakesShape(bool imageWasDrawnBefore)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var empty = SourceImage.Solid(context, Size, Size, Bgra.Transparent);
        using var source = new SourceImage(context, Size, Size, Scene);
        var effect = new InflateEffect();
        var expected = RenderFresh(context, effect, source);
        using var processor = effect.CreateVideoEffect(context);
        if (imageWasDrawnBefore)
        {
            processor.SetInput(source.Bitmap);
            RenderFrame(context, processor, 0);
        }
        processor.SetInput(empty.Bitmap);
        var before = RenderFrame(context, processor, 0);

        processor.SetInput(source.Bitmap);
        var after = RenderFrame(context, processor, 0);

        Assert.All(before.Coordinates(), point => Assert.Equal(0, before[point.X, point.Y].Alpha));
        Assert.True(Differs(after, source));
        Assert.True(after.SamePixelsAs(expected));
    }

    [Fact]
    public void ARecoloredImageIsDrawnLikeAFreshOne()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var amber = new SourceImage(context, Size, Size, Filled(Amber));
        using var azure = new SourceImage(context, Size, Size, Filled(Azure));
        var effect = new InflateEffect();
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(amber.Bitmap);
        RenderFrame(context, processor, 0);
        var expected = RenderFresh(context, effect, azure);

        processor.SetInput(azure.Bitmap);
        var recolored = RenderFrame(context, processor, 0);

        Assert.True(recolored.SamePixelsAs(expected));
    }

    [Fact]
    public void EveryFrameOfAnInputChangingInPlaceIsDrawnLikeAFreshProcessor()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var amber = new SourceImage(context, Size, Size, Scene);
        using var azure = new SourceImage(context, Size, Size, Filled(Azure));
        using var passThrough = new AffineTransform2D(context.DeviceContext);
        using var image = passThrough.Output;
        var effect = new InflateEffect();
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(image);
        var frames = new (SourceImage Source, Action<InflateEffect> Change)[]
        {
            (amber, _ => { }),
            (amber, _ => { }),
            (azure, _ => { }),
            (azure, effect => effect.Height.Values[0].Value = 30d),
            (amber, _ => { }),
            (amber, effect => effect.Amount.Values[0].Value = 50d),
            (amber, effect => effect.Specular.Values[0].Value = 0d),
            (amber, effect => effect.LightDirection.Values[0].Value = 90d),
            (amber, effect => effect.Specular.Values[0].Value = 80d),
            (amber, effect => effect.Quality = InflateQuality.Balanced),
            (azure, effect => effect.ColorKeep.Values[0].Value = 70d),
        };

        for (var index = 0; index < frames.Length; index++)
        {
            var (source, change) = frames[index];
            change(effect);
            passThrough.SetInput(0, source.Bitmap, true);

            var drawn = RenderFrame(context, processor, 0);
            using var fresh = effect.CreateVideoEffect(context);
            fresh.SetInput(image);
            var expected = RenderFrame(context, fresh, 0);

            Assert.True(drawn.SamePixelsAs(expected), $"frame {index}");
        }
    }

    [Fact]
    public void AProcessorWithoutAnInputHandsTheDrawDescriptionBack()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var processor = new InflateEffect().CreateVideoEffect(context);
        var description = EffectDescriptions.At(0, Length);

        var draw = processor.Update(description);

        Assert.Same(description.DrawDescription, draw);
    }

    [Fact]
    public void InflationReturnsOnceAnUnsupportedFrameIsFollowedByASupportedOne()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, 8193, Size, Scene);
        using var crop = new Crop(context.DeviceContext) { Rectangle = new Vector4(0f, 0f, Size, Size) };
        crop.SetInput(0, source.Bitmap, true);
        using var cropOutput = crop.Output;
        using var processor = new InflateEffect().CreateVideoEffect(context);
        processor.SetInput(cropOutput);
        var first = RenderFrame(context, processor, 0);

        crop.Rectangle = new Vector4(0f, 0f, 8193f, Size);
        var unsupported = RenderFrame(context, processor, 1);
        crop.Rectangle = new Vector4(0f, 0f, Size, Size);
        var again = RenderFrame(context, processor, 2);

        Assert.Equal((0, 0, 8193, Size), (unsupported.Left, unsupported.Top, unsupported.Width, unsupported.Height));
        Assert.True(unsupported.SamePixelsAs(Rendering.Capture(context, source.Bitmap)));
        Assert.False(first.SamePixelsAs(Rendering.Capture(context, cropOutput)));
        Assert.True(first.SamePixelsAs(again));
    }

    [Fact]
    public void AFailureWhileUpdatingIsNotSwallowed()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var processor = new InflateEffect().CreateVideoEffect(context);

        Assert.ThrowsAny<Exception>(() => processor.Update(null!));
    }
}
