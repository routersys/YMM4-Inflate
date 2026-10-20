namespace Inflate.Tests;

public sealed class InflateSettingsTests
{
    [Theory]
    [InlineData(1920, 1080, 1536, 1536, 864)]
    [InlineData(1080, 1920, 1536, 864, 1536)]
    [InlineData(1920, 1080, 1024, 1024, 576)]
    [InlineData(1920, 1080, 2048, 1920, 1080)]
    [InlineData(100, 50, 1536, 100, 50)]
    [InlineData(1, 1, 1024, 1, 1)]
    [InlineData(3, 2, 1024, 3, 2)]
    [InlineData(8192, 8, 1024, 1024, 1)]
    [InlineData(8, 8192, 1024, 1, 1024)]
    public void TheGridKeepsTheAspectRatioWithoutExceedingTheSourceOrTheResolution(int width, int height, int resolution, int expectedWidth, int expectedHeight)
    {
        var (gridWidth, gridHeight) = InflateSettings.GetGridSize(width, height, resolution);

        Assert.Equal((expectedWidth, expectedHeight), (gridWidth, gridHeight));
        Assert.True(gridWidth <= width && gridHeight <= height);
    }

    [Theory]
    [InlineData(16, 16, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(17, 16, 2)]
    [InlineData(16, 17, 2)]
    [InlineData(1536, 864, 8)]
    [InlineData(2048, 1152, 8)]
    [InlineData(1024, 576, 7)]
    public void TheLevelsHalveTheGridUntilTheLongSideFitsTheCoarsestSize(int width, int height, int expected)
    {
        Assert.Equal(expected, InflateSettings.GetLevelCount(width, height));
    }

    [Theory]
    [InlineData(1536, 864, 0, 1536, 864)]
    [InlineData(1536, 864, 1, 768, 432)]
    [InlineData(1536, 864, 7, 12, 7)]
    [InlineData(17, 5, 1, 9, 3)]
    [InlineData(1000, 80, 4, 63, 5)]
    [InlineData(1000, 80, 6, 16, 2)]
    [InlineData(300, 70, 3, 38, 9)]
    public void EveryLevelRoundsUpTheHalfOfTheLevelAbove(int width, int height, int level, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), InflateSettings.GetLevelSize(width, height, level));
    }

    [Theory]
    [InlineData(1536, 864)]
    [InlineData(17, 5)]
    [InlineData(300, 70)]
    [InlineData(2048, 1152)]
    [InlineData(1024, 1)]
    public void TheLevelsAreLaidOutOneAfterAnotherWithoutGapsInThePyramid(int width, int height)
    {
        var levelCount = InflateSettings.GetLevelCount(width, height);
        var expectedOffset = 0;

        for (var level = 0; level < levelCount; level++)
        {
            var (levelWidth, levelHeight) = InflateSettings.GetLevelSize(width, height, level);

            Assert.Equal(expectedOffset, InflateSettings.GetLevelOffset(width, height, level));
            expectedOffset += levelWidth * levelHeight;
        }

        Assert.Equal(expectedOffset, InflateSettings.GetPyramidLength(width, height));
    }

    [Theory]
    [InlineData(8192, 8192)]
    [InlineData(8192, 8)]
    [InlineData(8, 8192)]
    [InlineData(4096, 2731)]
    [InlineData(5, 4)]
    public void TheCoarsestLevelFitsOneThreadGroupForEveryQuality(int width, int height)
    {
        foreach (var quality in Enum.GetValues<InflateQuality>())
        {
            var (gridWidth, gridHeight) = InflateSettings.GetGridSize(width, height, InflateSettings.GetQuality(quality).GridResolution);
            var levelCount = InflateSettings.GetLevelCount(gridWidth, gridHeight);
            var (coarsestWidth, coarsestHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, levelCount - 1);

            Assert.True(coarsestWidth * coarsestHeight <= InflateSettings.CoarseSolveThreads, $"{quality} {coarsestWidth}x{coarsestHeight}");
        }
    }

    [Fact]
    public void ABetterQualityUsesAFinerGridAndMoreCycles()
    {
        var balanced = InflateSettings.GetQuality(InflateQuality.Balanced);
        var high = InflateSettings.GetQuality(InflateQuality.High);
        var ultra = InflateSettings.GetQuality(InflateQuality.Ultra);

        Assert.True(balanced.GridResolution < high.GridResolution && high.GridResolution < ultra.GridResolution);
        Assert.True(balanced.Cycles < high.Cycles && high.Cycles < ultra.Cycles);
        Assert.All(new[] { balanced, high, ultra }, settings => Assert.True(settings.Smoothing > 0));
    }

    [Theory]
    [InlineData(0f, 90f, 0f, 0f, 1f)]
    [InlineData(0f, 0f, 0f, -1f, 0f)]
    [InlineData(90f, 0f, 1f, 0f, 0f)]
    [InlineData(-90f, 0f, -1f, 0f, 0f)]
    [InlineData(180f, 0f, 0f, 1f, 0f)]
    public void TheLightComesFromTheGivenDirectionMeasuredClockwiseFromTheTop(float azimuth, float elevation, float x, float y, float z)
    {
        var light = InflateSettings.GetLightVector(azimuth, elevation);

        Assert.Equal(x, light.X, 5);
        Assert.Equal(y, light.Y, 5);
        Assert.Equal(z, light.Z, 5);
    }

    [Theory]
    [InlineData(-135f, 20f)]
    [InlineData(0f, 45f)]
    [InlineData(77f, 63f)]
    public void TheLightVectorIsAUnitVectorTiltedByTheElevation(float azimuth, float elevation)
    {
        var (x, y, z) = InflateSettings.GetLightVector(azimuth, elevation);

        Assert.Equal(1f, MathF.Sqrt(x * x + y * y + z * z), 5);
        Assert.Equal(MathF.Sin(elevation * MathF.PI / 180f), z, 5);
    }

    [Theory]
    [InlineData(-10f, 0f)]
    [InlineData(100f, 90f)]
    public void TheElevationIsHeldBetweenTheHorizonAndTheFront(float elevation, float expected)
    {
        var clamped = InflateSettings.GetLightVector(0f, elevation);
        var reference = InflateSettings.GetLightVector(0f, expected);

        Assert.Equal(reference, clamped);
    }

    [Fact]
    public void TheSpecularExponentRisesFromTheSmallestToTheLargestWithTheSharpness()
    {
        Assert.Equal(InflateSettings.MinimumSpecularExponent, InflateSettings.GetSpecularExponent(0f), 3);
        Assert.Equal(InflateSettings.MaximumSpecularExponent, InflateSettings.GetSpecularExponent(1f), 2);
        Assert.True(InflateSettings.GetSpecularExponent(0.3f) < InflateSettings.GetSpecularExponent(0.6f));
        Assert.Equal(InflateSettings.GetSpecularExponent(0f), InflateSettings.GetSpecularExponent(-1f));
        Assert.Equal(InflateSettings.GetSpecularExponent(1f), InflateSettings.GetSpecularExponent(2f));
    }

    [Theory]
    [InlineData(0f, 4f)]
    [InlineData(0.5f, 64f)]
    [InlineData(1f, 1024f)]
    public void TheSpecularExponentIsFourTimesTheSharpnessPowerOf256(float sharpness, float expected)
    {
        Assert.Equal(expected, InflateSettings.GetSpecularExponent(sharpness), 2);
    }

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(1, 4f)]
    [InlineData(2, 16f)]
    [InlineData(7, 16384f)]
    public void EachCoarserLevelQuadruplesTheRightHandSide(int level, float expected)
    {
        Assert.Equal(expected, InflateSettings.GetRightHandSide(level));
    }

    [Theory]
    [InlineData(1d, 1d, true)]
    [InlineData(8192d, 8192d, true)]
    [InlineData(8192d, 1d, true)]
    [InlineData(0d, 10d, false)]
    [InlineData(10d, 0d, false)]
    [InlineData(8193d, 10d, false)]
    [InlineData(10d, 8193d, false)]
    public void OnlyImagesWithinTheCanvasLimitAreSupported(double width, double height, bool expected)
    {
        Assert.Equal(expected, InflateSettings.IsSupportedSize(width, height));
    }
}
