using System.Runtime.InteropServices;
using ComputeWeave;

namespace Inflate.Tests;

[Collection("Direct3D12")]
public sealed class InflatePipelineTests
{
    const int Opaque = unchecked((int)0xFFC0C0C0);
    const int Red = unchecked((int)0xFFFF0000);
    const int Blue = unchecked((int)0xFF0000FF);

    static readonly bool Direct3D12IsAvailable = GraphicsDevice.EnumerateDevices().Any();

    static InflatePipeline CreatePipeline()
    {
        if (!Direct3D12IsAvailable)
            Assert.Skip("Direct3D 12 is unavailable.");
        var pipeline = InflatePipeline.TryCreate();
        Assert.NotNull(pipeline);
        return pipeline;
    }

    static InflatePipeline.Parameters Parameters(
        InflateQuality quality = InflateQuality.High,
        float height = 1f,
        float azimuth = -45f,
        float elevation = 50f,
        float ambient = 0.3f,
        float specular = 0.8f,
        float sharpness = 0.5f,
        float reflection = 0.3f,
        float rim = 0.2f,
        float colorKeep = 1f)
        => new(quality, height, azimuth, elevation, ambient, specular, sharpness, reflection, rim, colorKeep);

    static InflatePipeline.Parameters Plain(float azimuth = -45f, float elevation = 50f, float ambient = 0.3f, float colorKeep = 1f, float height = 1f)
        => Parameters(InflateQuality.High, height, azimuth, elevation, ambient, 0f, 0.5f, 0f, 0f, colorKeep);

    static int Alpha(int pixel) => (pixel >> 24) & 255;

    static int Red8(int pixel) => (pixel >> 16) & 255;

    static int Green8(int pixel) => (pixel >> 8) & 255;

    static int Blue8(int pixel) => pixel & 255;

    static int Brightness(int pixel) => Red8(pixel) + Green8(pixel) + Blue8(pixel);

    static int[] Uniform(int width, int height, int pixel) => Enumerable.Repeat(pixel, width * height).ToArray();

    static int[] Disc(int width, int height, double centerX, double centerY, double radius, int pixel)
    {
        var pixels = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var dx = x + 0.5 - centerX;
                var dy = y + 0.5 - centerY;
                if (dx * dx + dy * dy <= radius * radius)
                    pixels[y * width + x] = pixel;
            }
        }

        return pixels;
    }

    static int[] Rectangle(int width, int height, int left, int top, int right, int bottom, int pixel)
    {
        var pixels = new int[width * height];
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
                pixels[y * width + x] = pixel;
        }

        return pixels;
    }

    static int[] Ring(int width, int height, double radius, double hole, int pixel)
    {
        var pixels = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var dx = x + 0.5 - width / 2.0;
                var dy = y + 0.5 - height / 2.0;
                var distance = dx * dx + dy * dy;
                if (distance <= radius * radius && distance >= hole * hole)
                    pixels[y * width + x] = pixel;
            }
        }

        return pixels;
    }

    static int[] Mixed(int width, int height)
    {
        var source = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = x == 0 && y == 0 ? 0 : Math.Min(96 + (x + y) * 4, 255);
                var red = (x * 35 % 256) * alpha / 255;
                var green = (y * 35 % 256) * alpha / 255;
                var blue = ((x + y) * 17 % 256) * alpha / 255;
                source[y * width + x] = alpha << 24 | red << 16 | green << 8 | blue;
            }
        }

        return source;
    }

    static int[] Render(InflatePipeline pipeline, int[] source, int width, int height, InflatePipeline.Parameters parameters)
    {
        var destination = new int[source.Length];
        pipeline.Process(source, destination, width, height, in parameters);
        return destination;
    }

    static float[] ReferencePotential(int[] source, int width, int height)
    {
        var inside = source.Select(pixel => Alpha(pixel) >= 128).ToArray();
        var potential = new double[width * height];
        for (var sweep = 0; sweep < 60000; sweep++)
        {
            var change = 0d;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (!inside[index])
                        continue;

                    var outside = 0;
                    var sum = 0d;
                    if (x > 0 && inside[index - 1])
                        sum += potential[index - 1];
                    else
                        outside++;
                    if (x < width - 1 && inside[index + 1])
                        sum += potential[index + 1];
                    else
                        outside++;
                    if (y > 0 && inside[index - width])
                        sum += potential[index - width];
                    else
                        outside++;
                    if (y < height - 1 && inside[index + width])
                        sum += potential[index + width];
                    else
                        outside++;

                    var next = (1d + sum) / (4 + outside);
                    var updated = potential[index] + 1.6 * (next - potential[index]);
                    change = Math.Max(change, Math.Abs(updated - potential[index]));
                    potential[index] = updated;
                }
            }

            if (change < 1e-9)
                break;
        }

        return potential.Select(value => (float)value).ToArray();
    }

    static double MeanBrightness(int[] pixels, int width, int left, int top, int right, int bottom)
    {
        var total = 0d;
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (Alpha(pixels[y * width + x]) == 0)
                    continue;
                total += Brightness(pixels[y * width + x]);
                count++;
            }
        }

        return total / count;
    }

    [Theory]
    [InlineData(InflateQuality.Balanced)]
    [InlineData(InflateQuality.High)]
    [InlineData(InflateQuality.Ultra)]
    public void ATransparentSourceProducesATransparentImage(InflateQuality quality)
    {
        using var pipeline = CreatePipeline();
        var destination = Enumerable.Repeat(-1, 32 * 32).ToArray();
        var parameters = Parameters(quality);

        pipeline.Process(new int[32 * 32], destination, 32, 32, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void ATransparentSourceAfterAnOpaqueOneLeavesNothingBehind()
    {
        using var pipeline = CreatePipeline();
        var parameters = Parameters();
        Assert.Contains(Render(pipeline, Disc(48, 48, 24, 24, 16, Opaque), 48, 48, parameters), pixel => Alpha(pixel) > 0);

        var rendering = Render(pipeline, new int[48 * 48], 48, 48, parameters);

        Assert.All(rendering, pixel => Assert.Equal(0, pixel));
    }

    [Theory]
    [InlineData(InflateQuality.Balanced)]
    [InlineData(InflateQuality.Ultra)]
    public void TheOutputKeepsTheAlphaOfTheSourceAndStaysPremultiplied(InflateQuality quality)
    {
        using var pipeline = CreatePipeline();
        var source = Mixed(24, 16);

        var rendering = Render(pipeline, source, 24, 16, Parameters(quality, reflection: 0.8f, rim: 0.7f, specular: 2f));

        for (var index = 0; index < source.Length; index++)
        {
            Assert.InRange(Math.Abs(Alpha(rendering[index]) - Alpha(source[index])), 0, 1);
            Assert.InRange(Red8(rendering[index]), 0, Alpha(rendering[index]));
            Assert.InRange(Green8(rendering[index]), 0, Alpha(rendering[index]));
            Assert.InRange(Blue8(rendering[index]), 0, Alpha(rendering[index]));
        }
    }

    public static readonly TheoryData<string, InflateQuality, double> Shapes = new()
    {
        { "disc", InflateQuality.Balanced, 0.03 },
        { "disc", InflateQuality.High, 0.008 },
        { "disc", InflateQuality.Ultra, 0.002 },
        { "rectangle", InflateQuality.High, 0.012 },
        { "ring", InflateQuality.High, 0.008 },
        { "strip", InflateQuality.High, 0.004 },
    };

    static int[] ShapeOf(string name)
        => name switch
        {
            "disc" => Disc(128, 96, 64, 48, 40, Opaque),
            "rectangle" => Rectangle(128, 96, 10, 20, 100, 70, Opaque),
            "ring" => Ring(128, 96, 44, 18, Opaque),
            _ => Rectangle(128, 96, 4, 40, 124, 58, Opaque),
        };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ThePotentialMatchesTheDiscreteReference(string shape, InflateQuality quality, double tolerance)
    {
        using var pipeline = CreatePipeline();
        var source = ShapeOf(shape);
        var parameters = Parameters(quality);

        pipeline.Process(source, new int[source.Length], 128, 96, in parameters);
        var actual = pipeline.ReadPotential(128, 96, quality);
        var expected = ReferencePotential(source, 128, 96);

        var peak = expected.Max();
        var error = actual.Zip(expected, (a, e) => Math.Abs(a - e)).Max();
        Assert.True(error <= peak * tolerance, $"{shape} {quality} peak {peak} error {error}");
    }

    [Fact]
    public void ThePotentialOfADiscApproachesTheAnalyticSolution()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(128, 96, 64, 48, 40, Opaque);
        var parameters = Parameters(InflateQuality.Ultra);

        pipeline.Process(source, new int[source.Length], 128, 96, in parameters);
        var potential = pipeline.ReadPotential(128, 96, InflateQuality.Ultra);

        Assert.Equal(40d * 40d / 4d, potential[48 * 128 + 64], 400d * 0.01);
        Assert.Equal((40d * 40d - 20d * 20d) / 4d, potential[48 * 128 + 84], 300d * 0.03);
    }

    [Fact]
    public void TheHeightGrowsWithTheSquareOfTheWidthOfTheShape()
    {
        using var pipeline = CreatePipeline();
        var narrow = Rectangle(200, 80, 0, 30, 200, 50, Opaque);
        var wide = Rectangle(200, 80, 0, 20, 200, 60, Opaque);
        var parameters = Parameters(InflateQuality.Ultra);

        pipeline.Process(narrow, new int[narrow.Length], 200, 80, in parameters);
        var narrowPeak = pipeline.ReadPotential(200, 80, InflateQuality.Ultra)[40 * 200 + 100];
        pipeline.Process(wide, new int[wide.Length], 200, 80, in parameters);
        var widePeak = pipeline.ReadPotential(200, 80, InflateQuality.Ultra)[40 * 200 + 100];

        Assert.Equal(20d * 20d / 8d, narrowPeak, 50d * 0.03);
        Assert.Equal(40d * 40d / 8d, widePeak, 200d * 0.03);
        Assert.InRange(widePeak / narrowPeak, 3.6, 4.4);
    }

    [Fact]
    public void ThePotentialIsZeroOutsideTheShapeAndStaysNearlyNonNegativeInside()
    {
        using var pipeline = CreatePipeline();
        var source = Ring(128, 96, 44, 18, Opaque);
        var parameters = Parameters();

        pipeline.Process(source, new int[source.Length], 128, 96, in parameters);
        var potential = pipeline.ReadPotential(128, 96, parameters.Quality);

        var peak = potential.Max();
        for (var index = 0; index < source.Length; index++)
        {
            if (Alpha(source[index]) == 0)
                Assert.Equal(0f, potential[index]);
            else
                Assert.True(potential[index] >= -0.02f * peak, $"{index} {potential[index]}");
        }
    }

    [Fact]
    public void ARepeatedSolveGivesTheSamePotentialBitForBit()
    {
        using var pipeline = CreatePipeline();
        var source = Ring(128, 96, 44, 18, Opaque);
        var parameters = Parameters();

        pipeline.Process(source, new int[source.Length], 128, 96, in parameters);
        var first = pipeline.ReadPotential(128, 96, parameters.Quality);
        for (var run = 0; run < 4; run++)
        {
            pipeline.Process(source, new int[source.Length], 128, 96, in parameters);
            var again = pipeline.ReadPotential(128, 96, parameters.Quality);

            Assert.True(first.Zip(again).All(pair => BitConverter.SingleToInt32Bits(pair.First) == BitConverter.SingleToInt32Bits(pair.Second)));
        }
    }

    [Fact]
    public void MoreCyclesBringThePotentialCloserToTheReference()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(128, 96, 64, 48, 40, Opaque);
        var expected = ReferencePotential(source, 128, 96);
        var parameters = Parameters();
        var errors = new List<float>();

        foreach (var cycles in new[] { 1, 2, 3, 4 })
        {
            pipeline.CycleOverride = cycles;
            pipeline.Process(source, new int[source.Length], 128, 96, in parameters);
            errors.Add(pipeline.ReadPotential(128, 96, parameters.Quality).Zip(expected, (a, e) => Math.Abs(a - e)).Max());
        }

        Assert.True(errors.Zip(errors.Skip(1)).All(pair => pair.Second < pair.First), string.Join(", ", errors));
    }

    [Fact]
    public void ALongStripIsSolvedOnAGridCoarserThanItsPixels()
    {
        using var pipeline = CreatePipeline();
        var source = Rectangle(3072, 60, 0, 15, 3072, 45, Opaque);
        var gridSource = Rectangle(1024, 20, 0, 5, 1024, 15, Opaque);
        var parameters = Parameters(InflateQuality.Balanced);

        pipeline.Process(source, new int[source.Length], 3072, 60, in parameters);
        var potential = pipeline.ReadPotential(3072, 60, InflateQuality.Balanced);
        var expected = ReferencePotential(gridSource, 1024, 20);

        Assert.Equal((1024, 20), pipeline.GridSize);
        var peak = expected.Max();
        var error = potential.Zip(expected, (a, e) => Math.Abs(a - e)).Max();
        Assert.True(error <= peak * 0.06, $"peak {peak} error {error}");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void BarsThatAreFarLongerThanTheyAreWideAreSolvedAccurately(int thickness)
    {
        using var pipeline = CreatePipeline();
        var source = Rectangle(500, 40, 0, 10, 500, 10 + thickness, Opaque);
        var parameters = Parameters(InflateQuality.High);

        pipeline.Process(source, new int[source.Length], 500, 40, in parameters);
        var actual = pipeline.ReadPotential(500, 40, parameters.Quality);
        var expected = ReferencePotential(source, 500, 40);

        var peak = expected.Max();
        var error = actual.Zip(expected, (a, e) => Math.Abs(a - e)).Max();
        Assert.True(error <= peak * 0.03, $"thickness {thickness} peak {peak} error {error}");
    }

    static double ResidualDotSolution(int[] source, int width, int height, float[] solution)
    {
        var total = 0d;
        var load = 0d;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (Alpha(source[index]) < 128)
                    continue;

                var outside = 0;
                var sum = 0d;
                if (x > 0 && Alpha(source[index - 1]) >= 128)
                    sum += solution[index - 1];
                else
                    outside++;
                if (x < width - 1 && Alpha(source[index + 1]) >= 128)
                    sum += solution[index + 1];
                else
                    outside++;
                if (y > 0 && Alpha(source[index - width]) >= 128)
                    sum += solution[index - width];
                else
                    outside++;
                if (y < height - 1 && Alpha(source[index + width]) >= 128)
                    sum += solution[index + width];
                else
                    outside++;

                var residual = 1d - ((4 + outside) * solution[index] - sum);
                total += solution[index] * residual;
                load += solution[index];
            }
        }

        return total / load;
    }

    [Theory]
    [InlineData("disc")]
    [InlineData("rectangle")]
    [InlineData("ring")]
    public void TheCorrectionOfOneCycleIsScaledToMinimizeTheEnergy(string shape)
    {
        using var pipeline = CreatePipeline();
        var source = ShapeOf(shape);
        var parameters = Parameters(InflateQuality.High);
        pipeline.CycleOverride = 1;
        pipeline.SmoothingOverride = 0;

        pipeline.Process(source, new int[source.Length], 128, 96, in parameters);
        var solution = pipeline.ReadPotential(128, 96, parameters.Quality);

        Assert.Equal(0d, ResidualDotSolution(source, 128, 96, solution), 2);
    }

    [Fact]
    public void ASurfaceFacingAwayFromTheLightGetsNoHighlight()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(96, 96, 48, 48, 40, Opaque);
        var matte = Parameters(azimuth: -90f, elevation: 0f, ambient: 0f, specular: 0f, reflection: 0f, rim: 0f);
        var glossy = Parameters(azimuth: -90f, elevation: 0f, ambient: 0f, specular: 2f, sharpness: 0f, reflection: 0f, rim: 0f);

        var without = Render(pipeline, source, 96, 96, matte);
        var with = Render(pipeline, source, 96, 96, glossy);

        var awaySide = 48 * 96 + 54;
        var lightSide = 48 * 96 + 36;
        Assert.Equal(without[awaySide], with[awaySide]);
        Assert.True(Brightness(with[lightSide]) > Brightness(without[lightSide]) + 20);
    }

    [Fact]
    public void LightWrapsAroundASurfaceTurnedSlightlyAwayFromTheLight()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(96, 96, 48, 48, 40, Opaque);

        var rendering = Render(pipeline, source, 96, 96, Parameters(azimuth: -90f, elevation: 0f, ambient: 0f, specular: 0f, reflection: 0f, rim: 0f));

        Assert.True(Brightness(rendering[48 * 96 + 52]) > 30);
        Assert.True(Brightness(rendering[48 * 96 + 84]) < Brightness(rendering[48 * 96 + 52]) / 2);
    }

    [Fact]
    public void TwoPartsSeparatedByAOnePixelGapAreSolvedAsSeparateShapes()
    {
        using var pipeline = CreatePipeline();
        var source = new int[128 * 96];
        for (var y = 10; y < 86; y++)
        {
            for (var x = 6; x < 122; x++)
            {
                if (x != 63)
                    source[y * 128 + x] = Opaque;
            }
        }

        var parameters = Parameters(InflateQuality.High);

        pipeline.Process(source, new int[source.Length], 128, 96, in parameters);
        var actual = pipeline.ReadPotential(128, 96, parameters.Quality);
        var expected = ReferencePotential(source, 128, 96);

        var peak = expected.Max();
        var error = actual.Zip(expected, (a, e) => Math.Abs(a - e)).Max();
        Assert.True(error <= peak * 0.02, $"peak {peak} error {error}");
    }

    public static readonly TheoryData<string> AwkwardMasks = new() { "noise", "lines", "grid", "spiral" };

    static int[] AwkwardMask(string name)
    {
        var random = new Random(11);
        return name switch
        {
            "noise" => Enumerable.Range(0, 96 * 96).Select(_ => random.NextDouble() < 0.7 ? Opaque : 0).ToArray(),
            "lines" => Enumerable.Range(0, 200 * 80).Select(index => index / 200 == 40 || index / 200 is >= 10 and < 12 || index / 200 is >= 60 and < 63 ? Opaque : 0).ToArray(),
            "grid" => Enumerable.Range(0, 128 * 128).Select(index => index % 128 % 16 < 2 || index / 128 % 16 < 2 ? Opaque : 0).ToArray(),
            _ => Enumerable.Range(0, 160 * 160).Select(index =>
            {
                var dx = index % 160 + 0.5 - 80;
                var dy = index / 160 + 0.5 - 80;
                var turn = (Math.Sqrt(dx * dx + dy * dy) - Math.Atan2(dy, dx) * 10 / Math.PI) / 10;
                return Math.Sqrt(dx * dx + dy * dy) < 75 && (turn - Math.Floor(turn)) * 10 < 2 ? Opaque : 0;
            }).ToArray(),
        };
    }

    [Theory]
    [MemberData(nameof(AwkwardMasks))]
    public void MasksWithThinOrScatteredPartsConvergeWithoutOvershooting(string name)
    {
        using var pipeline = CreatePipeline();
        var source = AwkwardMask(name);
        var (width, height) = name switch { "noise" => (96, 96), "lines" => (200, 80), "grid" => (128, 128), _ => (160, 160) };
        var expected = ReferencePotential(source, width, height);
        var peak = expected.Max();
        var parameters = Parameters(InflateQuality.High);

        foreach (var cycles in new[] { 1, 2, 3, 6, 20 })
        {
            pipeline.CycleOverride = cycles;
            pipeline.Process(source, new int[source.Length], width, height, in parameters);
            var actual = pipeline.ReadPotential(width, height, parameters.Quality);

            Assert.True(actual.Min() >= -0.001f * peak, $"{name} {cycles} minimum {actual.Min()}");
            Assert.True(actual.Max() <= peak * 1.5f, $"{name} {cycles} maximum {actual.Max()} peak {peak}");
            if (cycles == 20)
                Assert.True(actual.Zip(expected, (a, e) => Math.Abs(a - e)).Max() <= peak * 0.01f, $"{name} error");
        }
    }

    [Theory]
    [InlineData(-90f)]
    [InlineData(90f)]
    [InlineData(0f)]
    [InlineData(180f)]
    public void TheSideFacingTheLightIsBrighterThanTheSideAwayFromIt(float azimuth)
    {
        using var pipeline = CreatePipeline();
        var source = Disc(96, 96, 48, 48, 40, Opaque);

        var rendering = Render(pipeline, source, 96, 96, Plain(azimuth, 30f));
        var left = MeanBrightness(rendering, 96, 8, 20, 36, 76);
        var right = MeanBrightness(rendering, 96, 60, 20, 88, 76);
        var top = MeanBrightness(rendering, 96, 20, 8, 76, 36);
        var bottom = MeanBrightness(rendering, 96, 20, 60, 76, 88);

        var (near, far) = azimuth switch
        {
            -90f => (left, right),
            90f => (right, left),
            0f => (top, bottom),
            _ => (bottom, top),
        };
        Assert.True(near > far * 1.15, $"{azimuth} {near} {far}");
    }

    [Fact]
    public void AZeroHeightShadesTheInsideOfTheShapeFlat()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(64, 64, 32, 32, 24, Opaque);

        var rendering = Render(pipeline, source, 64, 64, Plain(height: 0f));

        var inside = Enumerable.Range(0, rendering.Length).Where(index => Alpha(source[index]) == 255).Select(index => rendering[index]).Distinct().ToArray();
        Assert.Single(inside);
    }

    [Fact]
    public void FullAmbientLightShowsTheColorOfTheSourceOnEveryPixel()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(64, 64, 32, 32, 24, Opaque);

        var rendering = Render(pipeline, source, 64, 64, Plain(ambient: 1f));

        for (var index = 0; index < source.Length; index++)
        {
            if (Alpha(source[index]) != 255)
                continue;
            Assert.InRange(Math.Abs(Red8(rendering[index]) - Red8(Opaque)), 0, 1);
            Assert.InRange(Math.Abs(Green8(rendering[index]) - Green8(Opaque)), 0, 1);
            Assert.InRange(Math.Abs(Blue8(rendering[index]) - Blue8(Opaque)), 0, 1);
        }
    }

    [Fact]
    public void WithoutTheSourceColorTheSameShapeIsLitIdenticallyWhateverItsColor()
    {
        using var pipeline = CreatePipeline();
        var parameters = Plain(colorKeep: 0f);

        var red = Render(pipeline, Disc(64, 64, 32, 32, 24, Red), 64, 64, parameters);
        var blue = Render(pipeline, Disc(64, 64, 32, 32, 24, Blue), 64, 64, parameters);

        Assert.Equal(red, blue);
    }

    [Fact]
    public void WithTheSourceColorTheShadingKeepsTheHueOfTheSource()
    {
        using var pipeline = CreatePipeline();

        var rendering = Render(pipeline, Disc(64, 64, 32, 32, 24, Red), 64, 64, Plain());

        Assert.All(rendering.Where(pixel => Alpha(pixel) == 255), pixel =>
        {
            Assert.True(Red8(pixel) >= Green8(pixel));
            Assert.Equal(Green8(pixel), Blue8(pixel));
        });
        Assert.Contains(rendering, pixel => Red8(pixel) > 100 && Green8(pixel) == 0);
    }

    [Fact]
    public void TheGlossAddsABrighterHighlightThanTheShapeWithoutIt()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(96, 96, 48, 48, 40, Opaque);

        var matte = Render(pipeline, source, 96, 96, Parameters(specular: 0f, reflection: 0f, rim: 0f));
        var glossy = Render(pipeline, source, 96, 96, Parameters(specular: 2f, reflection: 0f, rim: 0f));

        Assert.True(glossy.Max(Brightness) > matte.Max(Brightness));
        Assert.True(Enumerable.Range(0, matte.Length).All(index => Brightness(glossy[index]) >= Brightness(matte[index]) - 3));
    }

    [Fact]
    public void ASharperGlossLeavesFewerPixelsInTheHighlight()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(96, 96, 48, 48, 40, Opaque);

        var soft = Render(pipeline, source, 96, 96, Parameters(specular: 2f, sharpness: 0.1f, ambient: 0f, reflection: 0f, rim: 0f));
        var sharp = Render(pipeline, source, 96, 96, Parameters(specular: 2f, sharpness: 0.9f, ambient: 0f, reflection: 0f, rim: 0f));
        var matte = Render(pipeline, source, 96, 96, Parameters(specular: 0f, ambient: 0f, reflection: 0f, rim: 0f));

        int Lit(int[] pixels) => Enumerable.Range(0, pixels.Length).Count(index => Brightness(pixels[index]) > Brightness(matte[index]) + 30);
        Assert.True(Lit(sharp) < Lit(soft), $"{Lit(sharp)} {Lit(soft)}");
        Assert.True(Lit(sharp) > 0);
    }

    [Fact]
    public void TheReflectionPullsTheColorTowardsTheSurroundings()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(96, 96, 48, 48, 40, Red);

        var without = Render(pipeline, source, 96, 96, Parameters(specular: 0f, reflection: 0f, rim: 0f));
        var with = Render(pipeline, source, 96, 96, Parameters(specular: 0f, reflection: 1f, rim: 0f));

        Assert.Contains(with, pixel => Alpha(pixel) == 255 && Blue8(pixel) > 0);
        Assert.All(without.Where(pixel => Alpha(pixel) == 255), pixel => Assert.Equal(0, Blue8(pixel)));
    }

    [Fact]
    public void TheRimLightBrightensTheEdgeMoreThanTheCenter()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(96, 96, 48, 48, 40, Red);

        var without = Render(pipeline, source, 96, 96, Parameters(specular: 0f, reflection: 0f, rim: 0f));
        var with = Render(pipeline, source, 96, 96, Parameters(specular: 0f, reflection: 0f, rim: 1f));

        var edge = 48 * 96 + 10;
        var center = 48 * 96 + 48;
        var edgeGain = Brightness(with[edge]) - Brightness(without[edge]);
        var centerGain = Brightness(with[center]) - Brightness(without[center]);
        Assert.True(edgeGain > 10, $"{edgeGain}");
        Assert.True(edgeGain > centerGain, $"{edgeGain} {centerGain}");
    }

    [Fact]
    public void ADimmerSourceOnlyScalesTheShadedColor()
    {
        using var pipeline = CreatePipeline();
        var opaque = Disc(64, 64, 32, 32, 24, unchecked((int)0xFF80C040));
        var dimmed = Disc(64, 64, 32, 32, 24, unchecked((int)0x994D7326));

        var full = Render(pipeline, opaque, 64, 64, Parameters());
        var partial = Render(pipeline, dimmed, 64, 64, Parameters());

        for (var index = 0; index < full.Length; index++)
        {
            if (Alpha(dimmed[index]) != 153)
                continue;
            Assert.InRange(Math.Abs(Red8(partial[index]) - Red8(full[index]) * 0.6), 0, 2.5);
            Assert.InRange(Math.Abs(Green8(partial[index]) - Green8(full[index]) * 0.6), 0, 2.5);
            Assert.InRange(Math.Abs(Blue8(partial[index]) - Blue8(full[index]) * 0.6), 0, 2.5);
        }
    }

    [Fact]
    public void ATallerHeightChangesTheShadingOfTheSameShape()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(96, 96, 48, 48, 40, Opaque);

        var flat = Render(pipeline, source, 96, 96, Plain(height: 0.1f, ambient: 0f));
        var tall = Render(pipeline, source, 96, 96, Plain(height: 3f, ambient: 0f));

        Assert.NotEqual(flat, tall);
        Assert.NotEqual(Brightness(flat[48 * 96 + 14]), Brightness(tall[48 * 96 + 14]));
    }

    [Fact]
    public void TheSpanAndTexturePathsAgreeOnEveryPixel()
    {
        using var pipeline = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        var source = Mixed(40, 28);
        var parameters = Parameters();
        var fromSpan = Render(pipeline, source, 40, 28, parameters);
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(40, 28);
        using var outputTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(40, 28);
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source));

        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, 40, 28, in parameters);
        var fromTexture = new int[40 * 28];
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(fromTexture.AsSpan()));

        Assert.Equal(fromSpan, fromTexture);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 1)]
    [InlineData(17, 1)]
    [InlineData(1, 17)]
    [InlineData(5, 4)]
    public void VeryFewPixelsAreStillInflatedWithoutLosingTheAlpha(int width, int height)
    {
        using var pipeline = CreatePipeline();
        var source = Uniform(width, height, Opaque);

        var rendering = Render(pipeline, source, width, height, Parameters());

        Assert.All(rendering, pixel => Assert.Equal(255, Alpha(pixel)));
        Assert.All(rendering, pixel => Assert.InRange(Red8(pixel), 1, 255));
    }

    [Fact]
    public void APipelineUsedAtAnotherSizeDrawsLikeAFreshOne()
    {
        using var pipeline = CreatePipeline();
        using var fresh = CreatePipeline();
        var small = Disc(32, 24, 16, 12, 9, Opaque);
        var large = Ring(96, 64, 28, 10, Opaque);
        Render(pipeline, small, 32, 24, Parameters());

        var reused = Render(pipeline, large, 96, 64, Parameters(InflateQuality.Ultra));
        var expected = Render(fresh, large, 96, 64, Parameters(InflateQuality.Ultra));

        Assert.Equal(expected, reused);
    }

    [Fact]
    public void TheBalancedQualityStaysCloseToTheShadingOfTheUltraQuality()
    {
        using var pipeline = CreatePipeline();
        var source = Disc(96, 96, 48, 48, 40, Opaque);

        var balanced = Render(pipeline, source, 96, 96, Parameters(InflateQuality.Balanced));
        var ultra = Render(pipeline, source, 96, 96, Parameters(InflateQuality.Ultra));

        var worst = Enumerable.Range(0, balanced.Length).Max(index => Math.Max(Math.Abs(Red8(balanced[index]) - Red8(ultra[index])), Math.Abs(Green8(balanced[index]) - Green8(ultra[index]))));
        Assert.True(worst <= 24, worst.ToString());
    }
}
