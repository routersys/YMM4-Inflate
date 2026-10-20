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
    public const int RowGroupWidth = 32;
    public const int RowGroupHeight = 2;
    public const int CorrectionGroupSize = 8;
    public const int CorrectionGroupThreads = CorrectionGroupSize * CorrectionGroupSize;
    public const int CorrectionReduceThreads = 256;
    public const float MaximumCorrectionScale = 4f;
    public const float MinimumInterpolationWeight = 1e-6f;
    public const float MinimumCorrectionEnergy = 1e-20f;
    public const float RadiansPerDegree = MathF.PI / 180f;
    public const float MaximumLightElevationDegrees = 90f;
    public const float LightAzimuthLimitDegrees = 180f;
    public const float MaximumHeight = 3f;
    public const float MaximumSpecular = 2f;
    public const uint MaskInside = 1u;
    public const int MaskOutsideShift = 1;
    public const int MaskOutsideWidth = 3;
    public const uint MaskOutsideBits = (1u << MaskOutsideWidth) - 1u;
    public const int MaskNeighborShift = MaskOutsideShift + MaskOutsideWidth;
    public const uint MaskLeft = 1u << MaskNeighborShift;
    public const uint MaskRight = MaskLeft << 1;
    public const uint MaskUp = MaskRight << 1;
    public const uint MaskDown = MaskUp << 1;
    public const float PotentialEpsilon = 0.0625f;
    public const float LightWrap = 0.35f;
    public const float ReflectionBase = 0.15f;
    public const float SurfaceWhite = 0.9f;
    public const float SkyBlendStart = 0.25f;
    public const float SkyBlendEnd = 0.75f;
    public const float RimWhiteMix = 0.5f;
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
        => (CeilDivide(width, 2), CeilDivide(height, 2));

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

    public static int GetCorrectionGroupColumns(int gridWidth)
        => CeilDivide(gridWidth, CorrectionGroupSize);

    public static int GetCorrectionGroupCount(int gridWidth, int gridHeight)
        => GetCorrectionGroupColumns(gridWidth) * CeilDivide(gridHeight, CorrectionGroupSize);

    public static int CeilDivide(int value, int divisor)
        => (value + divisor - 1) / divisor;

    public static bool IsSupportedSize(double width, double height)
        => width >= 1d && height >= 1d &&
            width <= MaximumCanvasSize && height <= MaximumCanvasSize;

    public static (float X, float Y, float Z) GetLightVector(float azimuthDegrees, float elevationDegrees)
    {
        var azimuth = azimuthDegrees * RadiansPerDegree;
        var elevation = Math.Clamp(elevationDegrees, 0f, MaximumLightElevationDegrees) * RadiansPerDegree;
        var horizontal = MathF.Cos(elevation);
        return (MathF.Sin(azimuth) * horizontal, -MathF.Cos(azimuth) * horizontal, MathF.Sin(elevation));
    }

    public static float GetSpecularExponent(float sharpness)
        => MinimumSpecularExponent * MathF.Pow(MaximumSpecularExponent / MinimumSpecularExponent, Math.Clamp(sharpness, 0f, 1f));

    internal readonly record struct QualitySettings(int GridResolution, int Cycles, int Smoothing);
}
