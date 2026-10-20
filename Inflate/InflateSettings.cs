namespace Inflate;

internal static class InflateSettings
{
    public const int MaximumCanvasSize = 8192;
    public const int CoarsestLevelSize = 16;
    public const int CoarseSolveThreads = CoarsestLevelSize * CoarsestLevelSize;
    public const int CoarseSolveRegions = 3;
    public const int CoarseSolveIterations = 256;
    public const float CoverageThreshold = 0.5f;
    public const uint CoarseSolidCount = 3u;
    public const uint CoarsePairCount = 2u;
    public const int CorrectionGroupSize = 8;
    public const int CorrectionGroupThreads = CorrectionGroupSize * CorrectionGroupSize;
    public const int CorrectionReduceThreads = 256;
    public const float MaximumCorrectionScale = 4f;
    public const uint MaskInside = 1u;
    public const int MaskOutsideShift = 1;
    public const uint MaskOutsideBits = 7u;
    public const uint MaskLeft = 16u;
    public const uint MaskRight = 32u;
    public const uint MaskUp = 64u;
    public const uint MaskDown = 128u;
    public const float PotentialEpsilon = 0.0625f;
    public const float LightWrap = 0.35f;
    public const float ReflectionBase = 0.15f;
    public const float SurfaceWhite = 0.9f;
    public const float MinimumSpecularExponent = 4f;
    public const float MaximumSpecularExponent = 1024f;
    public const float SkyRed = 0.82f;
    public const float SkyGreen = 0.9f;
    public const float SkyBlue = 1f;
    public const float GroundRed = 0.32f;
    public const float GroundGreen = 0.28f;
    public const float GroundBlue = 0.25f;
    public const int MaximumPendingSubmissions = 32;
    public const int SourceHashLength = 4;
    public const int ShapeHashSum = 0;
    public const int ShapeHashMix = 1;
    public const int ContentHashSum = 2;
    public const int ContentHashMix = 3;

    public static QualitySettings GetQuality(InflateQuality quality)
        => quality switch
        {
            InflateQuality.Balanced => new QualitySettings(1024, 2, 3),
            InflateQuality.Ultra => new QualitySettings(2048, 4, 4),
            _ => new QualitySettings(1536, 3, 3),
        };

    public static (int Width, int Height) GetGridSize(int width, int height, int resolution)
    {
        var longSide = Math.Max(width, height);
        var shortSide = Math.Max(Math.Min(width, height), 1);
        var gridLong = Math.Max(Math.Min(resolution, longSide), 1);
        var gridShort = Math.Clamp((int)Math.Round((double)gridLong * shortSide / longSide), 1, gridLong);
        return width >= height ? (gridLong, gridShort) : (gridShort, gridLong);
    }

    public static (int Width, int Height) GetCoarserLevelSize(int width, int height)
        => ((width + 1) / 2, (height + 1) / 2);

    public static int GetLevelCount(int gridWidth, int gridHeight)
    {
        var count = 1;
        var (width, height) = (gridWidth, gridHeight);
        while (Math.Max(width, height) > CoarsestLevelSize)
        {
            (width, height) = GetCoarserLevelSize(width, height);
            count++;
        }
        return count;
    }

    public static (int Width, int Height) GetLevelSize(int gridWidth, int gridHeight, int level)
    {
        var (width, height) = (gridWidth, gridHeight);
        for (var index = 0; index < level; index++)
            (width, height) = GetCoarserLevelSize(width, height);
        return (width, height);
    }

    public static int GetLevelOffset(int gridWidth, int gridHeight, int level)
    {
        var offset = 0;
        var (width, height) = (gridWidth, gridHeight);
        for (var index = 0; index < level; index++)
        {
            offset += width * height;
            (width, height) = GetCoarserLevelSize(width, height);
        }
        return offset;
    }

    public static float GetRightHandSide(int level)
        => 1 << (2 * level);

    public static int GetPyramidLength(int gridWidth, int gridHeight)
        => GetLevelOffset(gridWidth, gridHeight, GetLevelCount(gridWidth, gridHeight));

    public static int GetCorrectionGroupCount(int gridWidth, int gridHeight)
        => (gridWidth + CorrectionGroupSize - 1) / CorrectionGroupSize * ((gridHeight + CorrectionGroupSize - 1) / CorrectionGroupSize);

    public static bool IsSupportedSize(double width, double height)
        => width >= 1d && height >= 1d &&
            width <= MaximumCanvasSize && height <= MaximumCanvasSize;

    public static (float X, float Y, float Z) GetLightVector(float azimuthDegrees, float elevationDegrees)
    {
        var azimuth = azimuthDegrees * MathF.PI / 180f;
        var elevation = Math.Clamp(elevationDegrees, 0f, 90f) * MathF.PI / 180f;
        var horizontal = MathF.Cos(elevation);
        return (MathF.Sin(azimuth) * horizontal, -MathF.Cos(azimuth) * horizontal, MathF.Sin(elevation));
    }

    public static float GetSpecularExponent(float sharpness)
        => MinimumSpecularExponent * MathF.Pow(MaximumSpecularExponent / MinimumSpecularExponent, Math.Clamp(sharpness, 0f, 1f));

    internal readonly record struct QualitySettings(int GridResolution, int Cycles, int Smoothing);
}
