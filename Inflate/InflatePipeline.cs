using System.ComponentModel;
using System.Runtime.InteropServices;
using ComputeWeave;

namespace Inflate;

internal sealed class InflatePipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly InflatePipelineHost _host;
    private readonly ReadWriteBuffer<int> _syncBuffer;
    private readonly ReadWriteBuffer<int> _sourceHash;
    private readonly ReadBackBuffer<int> _sourceHashReadBack;
    private StructureKey? _structureKey;
    private ComputeSubmission _pendingHash;
    private bool _hasPendingHash;
    private SourceHashes? _lastHashes;
    private bool _shapeChanging;
    private int _gridWidth;
    private int _gridHeight;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;

    private InflatePipeline(GraphicsDevice device, InflatePipelineHost host)
    {
        _device = device;
        _host = host;
        _syncBuffer = device.AllocateReadWriteBuffer<int>(1);
        _sourceHash = device.AllocateReadWriteBuffer<int>(InflateSettings.SourceHashLength);
        _sourceHashReadBack = device.AllocateReadBackBuffer<int>(InflateSettings.SourceHashLength);
    }

    internal void WaitForCompletion() => SynchronizeDevice();

    private void SynchronizeDevice()
    {
        using ComputeContext context = _device.CreateComputeContext();
        context.Clear(_syncBuffer);
    }

    public static InflatePipeline? TryCreate()
    {
        try
        {
            return TryCreate(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static InflatePipeline? TryCreate(GraphicsDevice device)
    {
        InflatePipelineHost? host = null;
        try
        {
            host = InflatePipelineHost.Create(device, InflateSettings.MaximumPendingSubmissions);
            var pipeline = new InflatePipeline(device, host);
            host = null;
            return pipeline;
        }
        catch (Win32Exception)
        {
            return null;
        }
        finally
        {
            host?.Dispose();
            host?.WaitForDisposal();
        }
    }

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in Parameters parameters)
    {
        var pixelCount = checked(width * height);
        EnsureResources(width, height, parameters.Quality);
        EnsurePackedTextures(width, height);
        var sourceTexture = _packedSource!;
        var outputTexture = _packedOutput!;
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source[..pixelCount]));
        SubmitFullPipeline(sourceTexture, outputTexture, width, height, in parameters).Wait();
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(destination[..pixelCount]));
    }

    public void Process(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureResources(width, height, parameters.Quality);
        _ = SubmitFullPipeline(source, destination, width, height, in parameters);
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureResources(width, height, parameters.Quality);
        SubmitFullPipeline(source, destination, width, height, in parameters).Wait();
    }

    public void Process(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> source,
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        _ = Simulate(source, width, height, in parameters);
        Render(source, destination, width, height, in parameters);
    }

    internal SimulationResult Simulate(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> source,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureResources(width, height, parameters.Quality);
        var derived = Derive(in parameters);
        ResolvePendingHash();
        var covered = _host.RecordSharedCoverage(source, _sourceHash, width, height, in derived);
        var structure = new StructureParameters(width, height, parameters.Quality);
        SourceHashes? hashes = null;
        if (!_shapeChanging && _structureKey is { Shape: not null } current && current.Parameters == structure)
        {
            covered.Wait();
            hashes = ReadHashes();
            if (current.Shape == hashes.Value.Shape)
                return new SimulationResult(false, hashes.Value.Content);
        }
        else
        {
            _pendingHash = covered;
            _hasPendingHash = true;
        }

        _ = _host.RecordSolve(in derived);
        _structureKey = new StructureKey(structure, hashes?.Shape);
        return new SimulationResult(true, hashes?.Content);
    }

    private void ResolvePendingHash()
    {
        if (!_hasPendingHash)
            return;

        _hasPendingHash = false;
        _pendingHash.Wait();
        var hashes = ReadHashes();
        if (_structureKey is { Shape: null } key)
            _structureKey = key with { Shape = hashes.Shape };
    }

    private SourceHashes ReadHashes()
    {
        _sourceHashReadBack.CopyFrom(_sourceHash);
        var hashed = _sourceHashReadBack.Span;
        var hashes = new SourceHashes(
            new ContentHash(hashed[InflateSettings.ShapeHashSum], hashed[InflateSettings.ShapeHashMix]),
            new ContentHash(hashed[InflateSettings.ContentHashSum], hashed[InflateSettings.ContentHashMix]));
        _shapeChanging = _lastHashes is { } last && last.Shape != hashes.Shape;
        _lastHashes = hashes;
        return hashes;
    }

    internal void Render(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> source,
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> output,
        int width,
        int height,
        in Parameters parameters)
    {
        var derived = Derive(in parameters);
        _ = _host.RecordSharedRender(source, output, width, height, in derived, in parameters);
    }

    internal float[] ReadPotential(int width, int height, InflateQuality quality)
    {
        EnsureResources(width, height, quality);
        var length = _gridWidth * _gridHeight;
        using var destination = _device.AllocateReadWriteBuffer<float>(length);
        _host.RecordReadPotential(destination, length).Wait();
        var values = new float[length];
        destination.CopyTo(values);
        return values;
    }

    internal (int Width, int Height) GridSize => (_gridWidth, _gridHeight);

    internal int? CycleOverride { get; set; }

    internal int? SmoothingOverride { get; set; }

    private ComputeSubmission SubmitFullPipeline(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        _structureKey = null;
        _hasPendingHash = false;
        _lastHashes = null;
        _shapeChanging = false;
        var derived = Derive(in parameters);
        return _host.RecordFullPipeline(source, output, _sourceHash, width, height, in derived, in parameters);
    }

    private DerivedValues Derive(in Parameters parameters)
    {
        var settings = InflateSettings.GetQuality(parameters.Quality);
        return new DerivedValues(
            _gridWidth,
            _gridHeight,
            InflateSettings.GetLevelCount(_gridWidth, _gridHeight),
            CycleOverride ?? settings.Cycles,
            SmoothingOverride ?? settings.Smoothing);
    }

    private void EnsureResources(int width, int height, InflateQuality quality)
    {
        var settings = InflateSettings.GetQuality(quality);
        var (gridWidth, gridHeight) = InflateSettings.GetGridSize(width, height, settings.GridResolution);
        var levelCount = InflateSettings.GetLevelCount(gridWidth, gridHeight);
        if (levelCount > InflateSettings.MaximumLevelCount)
            throw new InvalidOperationException();

        var pyramidLength = InflateSettings.GetPyramidLength(gridWidth, gridHeight);
        if (!_host.TryEnsureGrid(
                new InflateGridResources.Plan(
                    maskLength: pyramidLength,
                    potentialLength: pyramidLength,
                    residualLength: pyramidLength,
                    partialLength: 2 * InflateSettings.GetCorrectionGroupCount(gridWidth, gridHeight),
                    scaleLength: 1),
                out var changed))
            throw new InvalidOperationException();

        if (changed)
            _structureKey = null;
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
    }

    private void EnsurePackedTextures(int width, int height)
    {
        if (_packedWidth == width && _packedHeight == height)
            return;

        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedOutput = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedWidth = width;
        _packedHeight = height;
    }

    public void Dispose()
    {
        _host.Dispose();
        _host.WaitForDisposal();
        _structureKey = null;
        _gridWidth = 0;
        _gridHeight = 0;
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
        _syncBuffer.Dispose();
        _sourceHash.Dispose();
        _sourceHashReadBack.Dispose();
    }

    internal readonly record struct ContentHash(int Sum, int Mix);

    private readonly record struct SourceHashes(ContentHash Shape, ContentHash Content);

    internal readonly record struct SimulationResult(bool StructureChanged, ContentHash? Content);

    private readonly record struct StructureParameters(
        int Width,
        int Height,
        InflateQuality Quality);

    private readonly record struct StructureKey(
        StructureParameters Parameters,
        ContentHash? Shape);

    internal readonly record struct DerivedValues(
        int GridWidth,
        int GridHeight,
        int LevelCount,
        int Cycles,
        int Smoothing);

    internal readonly record struct Parameters(
        InflateQuality Quality,
        float Height,
        float LightAzimuth,
        float LightElevation,
        float Ambient,
        float Specular,
        float Sharpness,
        float Reflection,
        float Rim,
        float ColorKeep);
}
