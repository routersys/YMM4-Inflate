using ComputeWeave;

namespace Inflate;

[ComputeResourceGroup]
internal sealed partial class InflateGridResources
{
    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<uint> Mask { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Potential { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Residual { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Partial { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Scale { get; }
}

[ComputePipelineHost("_device", 1)]
internal sealed partial class InflatePipelineHost
{
    private readonly GraphicsDevice _device;

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite, ComputeResourceRecovery.Recompute)]
    private readonly ComputeResourceGroupSlot<InflateGridResources> _grid = new();

    [ComputePipeline]
    private void RecordFullPipeline(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InflateGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> sourceHash,
        int width,
        int height,
        in InflatePipeline.DerivedValues derived,
        in InflatePipeline.Parameters parameters)
    {
        _ = _device;

        RecordCoverageStage(in context, grid, source, sourceHash, width, height, in derived);
        RecordSolveStage(in context, grid, in derived);
        RecordRenderStage(in context, grid, source, output, width, height, in derived, in parameters);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedCoverage(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InflateGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> sourceHash,
        int width,
        int height,
        in InflatePipeline.DerivedValues derived)
    {
        _ = _device;

        RecordCoverageStage(in context, grid, source, sourceHash, width, height, in derived);
    }

    [ComputePipeline]
    private void RecordSolve(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InflateGridResources grid,
        in InflatePipeline.DerivedValues derived)
    {
        _ = _device;

        RecordSolveStage(in context, grid, in derived);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedRender(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InflateGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in InflatePipeline.DerivedValues derived,
        in InflatePipeline.Parameters parameters)
    {
        _ = _device;

        RecordRenderStage(in context, grid, source, output, width, height, in derived, in parameters);
    }

    [ComputePipeline]
    private void RecordReadPotential(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InflateGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<float> destination,
        int length)
    {
        _ = _device;

        context.For(length, new CopyShader(grid.Potential, destination, length));
    }

    private static void RecordCoverageStage(
        in ComputeContext context,
        InflateGridResources grid,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteBuffer<int> sourceHash,
        int width,
        int height,
        in InflatePipeline.DerivedValues derived)
    {
        var gridWidth = derived.GridWidth;
        var gridHeight = derived.GridHeight;

        context.Clear(sourceHash);
        context.Barrier(sourceHash);
        context.For(
            ThreadGroupAlignment.AlignX<CoverageShader>(gridWidth),
            ThreadGroupAlignment.AlignY<CoverageShader>(gridHeight),
            new CoverageShader(source, grid.Mask, sourceHash, width, height, gridWidth, gridHeight));
        context.Barrier(grid.Mask);
    }

    private static void RecordSolveStage(
        in ComputeContext context,
        InflateGridResources grid,
        in InflatePipeline.DerivedValues derived)
    {
        var gridWidth = derived.GridWidth;
        var gridHeight = derived.GridHeight;
        var levelCount = derived.LevelCount;
        var coarsestLevel = levelCount - 1;

        context.Clear(grid.Potential);
        context.Barrier(grid.Potential);

        context.For(
            gridWidth,
            gridHeight,
            new MaskNeighborShader(grid.Mask, 0, gridWidth, gridHeight));
        context.Barrier(grid.Mask);

        for (var level = 1; level < levelCount; level++)
        {
            var (fineWidth, fineHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, level - 1);
            var (coarseWidth, coarseHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, level);
            var fineOffset = InflateSettings.GetLevelOffset(gridWidth, gridHeight, level - 1);
            var coarseOffset = InflateSettings.GetLevelOffset(gridWidth, gridHeight, level);
            context.For(
                coarseWidth,
                coarseHeight,
                new MaskRestrictShader(grid.Mask, fineOffset, fineWidth, fineHeight, coarseOffset, coarseWidth, coarseHeight));
            context.Barrier(grid.Mask);
            context.For(
                coarseWidth,
                coarseHeight,
                new CoarseNeighborShader(grid.Mask, fineOffset, fineWidth, fineHeight, coarseOffset, coarseWidth, coarseHeight));
            context.Barrier(grid.Mask);
        }

        if (coarsestLevel == 0)
        {
            context.For(
                gridWidth,
                gridHeight,
                new ResidualShader(grid.Potential, grid.Mask, grid.Residual, 0, gridWidth, gridHeight, InflateSettings.GetRightHandSide(0)));
            context.Barrier(grid.Residual);
            RecordCoarseSolve(in context, grid, 0, gridWidth, gridHeight);
            return;
        }

        for (var cycle = 0; cycle < derived.Cycles; cycle++)
            RecordCycle(in context, grid, gridWidth, gridHeight, coarsestLevel, 0, derived.Smoothing);
    }

    private static void RecordCycle(
        in ComputeContext context,
        InflateGridResources grid,
        int gridWidth,
        int gridHeight,
        int coarsestLevel,
        int topLevel,
        int sweeps)
    {
        var (topWidth, topHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, topLevel);
        var topOffset = InflateSettings.GetLevelOffset(gridWidth, gridHeight, topLevel);
        var topRightHandSide = InflateSettings.GetRightHandSide(topLevel);
        context.Barrier(grid.Residual);
        context.For(
            topWidth,
            topHeight,
            new ResidualShader(grid.Potential, grid.Mask, grid.Residual, topOffset, topWidth, topHeight, topRightHandSide));
        context.Barrier(grid.Residual);

        for (var level = topLevel + 1; level <= coarsestLevel; level++)
        {
            var (fineWidth, fineHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, level - 1);
            var (coarseWidth, coarseHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, level);
            context.For(
                coarseWidth,
                coarseHeight,
                new RestrictResidualShader(
                    grid.Residual,
                    grid.Mask,
                    InflateSettings.GetLevelOffset(gridWidth, gridHeight, level - 1),
                    fineWidth,
                    fineHeight,
                    InflateSettings.GetLevelOffset(gridWidth, gridHeight, level),
                    coarseWidth,
                    coarseHeight));
            context.Barrier(grid.Residual);
        }

        var (coarsestWidth, coarsestHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, coarsestLevel);
        RecordCoarseSolve(in context, grid, InflateSettings.GetLevelOffset(gridWidth, gridHeight, coarsestLevel), coarsestWidth, coarsestHeight);

        for (var level = coarsestLevel - 1; level >= topLevel; level--)
        {
            if (level == topLevel)
                RecordCorrectionScale(in context, grid, gridWidth, gridHeight, level + 1);
            RecordProlong(in context, grid, gridWidth, gridHeight, level + 1, level, level == topLevel);
            var (levelWidth, levelHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, level);
            RecordSmoothing(in context, grid, InflateSettings.GetLevelOffset(gridWidth, gridHeight, level), levelWidth, levelHeight, level == topLevel, topRightHandSide, sweeps);
        }
    }

    private static void RecordCoarseSolve(
        in ComputeContext context,
        InflateGridResources grid,
        int offset,
        int width,
        int height)
    {
        context.For(
            InflateSettings.CoarseSolveThreads,
            new CoarseSolveShader(grid.Mask, grid.Residual, grid.Potential, offset, width, height, InflateSettings.CoarseSolveIterations));
        context.Barrier(grid.Potential);
    }

    private static void RecordCorrectionScale(
        in ComputeContext context,
        InflateGridResources grid,
        int gridWidth,
        int gridHeight,
        int coarseLevel)
    {
        var (coarseWidth, coarseHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, coarseLevel);
        var groupCount = InflateSettings.GetCorrectionGroupCount(gridWidth, gridHeight);
        context.Barrier(grid.Partial);
        context.For(
            ThreadGroupAlignment.AlignX<CorrectionDotShader>(gridWidth),
            ThreadGroupAlignment.AlignY<CorrectionDotShader>(gridHeight),
            new CorrectionDotShader(
                grid.Potential,
                grid.Mask,
                grid.Residual,
                grid.Partial,
                InflateSettings.GetLevelOffset(gridWidth, gridHeight, coarseLevel),
                coarseWidth,
                coarseHeight,
                gridWidth,
                gridHeight,
                InflateSettings.GetCorrectionGroupColumns(gridWidth)));
        context.Barrier(grid.Partial);
        context.Barrier(grid.Scale);
        context.For(
            InflateSettings.CorrectionReduceThreads,
            new CorrectionScaleShader(grid.Partial, grid.Scale, groupCount, InflateSettings.MaximumCorrectionScale));
        context.Barrier(grid.Scale);
    }

    private static void RecordProlong(
        in ComputeContext context,
        InflateGridResources grid,
        int gridWidth,
        int gridHeight,
        int coarseLevel,
        int fineLevel,
        bool accumulate)
    {
        var (coarseWidth, coarseHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, coarseLevel);
        var (fineWidth, fineHeight) = InflateSettings.GetLevelSize(gridWidth, gridHeight, fineLevel);
        context.For(
            fineWidth,
            fineHeight,
            new ProlongShader(
                grid.Potential,
                grid.Mask,
                grid.Scale,
                InflateSettings.GetLevelOffset(gridWidth, gridHeight, coarseLevel),
                coarseWidth,
                coarseHeight,
                InflateSettings.GetLevelOffset(gridWidth, gridHeight, fineLevel),
                fineWidth,
                fineHeight,
                accumulate ? 1 : 0));
        context.Barrier(grid.Potential);
    }

    private static void RecordSmoothing(
        in ComputeContext context,
        InflateGridResources grid,
        int offset,
        int width,
        int height,
        bool constantRhs,
        float rhsValue,
        int sweeps)
    {
        for (var sweep = 0; sweep < sweeps; sweep++)
        {
            for (var color = 0; color < 2; color++)
            {
                context.For(
                    InflateSettings.CeilDivide(width, 2),
                    height,
                    new GaussSeidelShader(grid.Mask, grid.Residual, grid.Potential, offset, width, height, color, constantRhs ? 1 : 0, rhsValue));
                context.Barrier(grid.Potential);
            }
        }
    }

    private static void RecordRenderStage(
        in ComputeContext context,
        InflateGridResources grid,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in InflatePipeline.DerivedValues derived,
        in InflatePipeline.Parameters parameters)
    {
        var (lightX, lightY, lightZ) = InflateSettings.GetLightVector(parameters.LightAzimuth, parameters.LightElevation);
        context.For(
            width,
            height,
            new RenderShader(
                source,
                grid.Potential,
                output,
                width,
                height,
                derived.GridWidth,
                derived.GridHeight,
                parameters.Height,
                lightX,
                lightY,
                lightZ,
                parameters.Ambient,
                parameters.Specular,
                InflateSettings.GetSpecularExponent(parameters.Sharpness),
                parameters.Reflection,
                parameters.Rim,
                parameters.ColorKeep));
    }
}
