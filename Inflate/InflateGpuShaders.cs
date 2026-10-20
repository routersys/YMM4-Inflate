// Stryker disable all
using ComputeWeave;

namespace Inflate;

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CoverageShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<uint> mask,
    ReadWriteBuffer<int> sourceHash,
    int width,
    int height,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<uint> mask = mask;
    private readonly ReadWriteBuffer<int> sourceHash = sourceHash;
    private readonly int width = width;
    private readonly int height = height;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    [GroupShared(InflateSettings.SourceHashLength)]
    private static readonly int[] groupHash = null!;

    public void Execute()
    {
        if (GroupIds.Index == 0)
        {
            groupHash[0] = 0;
            groupHash[1] = 0;
            groupHash[2] = 0;
            groupHash[3] = 0;
        }
        Hlsl.GroupMemoryBarrierWithGroupSync();

        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx < gridWidth && gy < gridHeight)
        {
            var x0 = gx * width / gridWidth;
            var x1 = (gx + 1) * width / gridWidth;
            var y0 = gy * height / gridHeight;
            var y1 = (gy + 1) * height / gridHeight;

            var alphaSum = 0f;
            var shapeSum = 0u;
            var shapeMix = 0u;
            var contentSum = 0u;
            var contentMix = 0u;
            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    var pixel = source[new Int2(x, y)];
                    var index = (uint)(y * width + x);
                    alphaSum += pixel.W;
                    var shape = InflateShaderMath.MixAlpha(index, pixel.W);
                    var content = InflateShaderMath.MixTexel(index, pixel);
                    shapeSum += shape;
                    shapeMix ^= shape * 0xC2B2AE35u;
                    contentSum += content;
                    contentMix ^= content * 0xC2B2AE35u;
                }
            }

            var count = (x1 - x0) * (y1 - y0);
            mask[gy * gridWidth + gx] = alphaSum >= count * InflateSettings.CoverageThreshold ? InflateSettings.MaskInside : 0u;
            Hlsl.InterlockedAdd(ref groupHash[0], (int)shapeSum);
            Hlsl.InterlockedXor(ref groupHash[1], (int)shapeMix);
            Hlsl.InterlockedAdd(ref groupHash[2], (int)contentSum);
            Hlsl.InterlockedXor(ref groupHash[3], (int)contentMix);
        }
        Hlsl.GroupMemoryBarrierWithGroupSync();

        if (GroupIds.Index != 0)
            return;
        Hlsl.InterlockedAdd(ref sourceHash[0], groupHash[0]);
        Hlsl.InterlockedXor(ref sourceHash[1], groupHash[1]);
        Hlsl.InterlockedAdd(ref sourceHash[2], groupHash[2]);
        Hlsl.InterlockedXor(ref sourceHash[3], groupHash[3]);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MaskRestrictShader(
    ReadWriteBuffer<uint> mask,
    int fineOffset,
    int fineWidth,
    int fineHeight,
    int coarseOffset,
    int coarseWidth,
    int coarseHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> mask = mask;
    private readonly int fineOffset = fineOffset;
    private readonly int fineWidth = fineWidth;
    private readonly int fineHeight = fineHeight;
    private readonly int coarseOffset = coarseOffset;
    private readonly int coarseWidth = coarseWidth;
    private readonly int coarseHeight = coarseHeight;

    public void Execute()
    {
        var cx = ThreadIds.X;
        var cy = ThreadIds.Y;
        if (cx >= coarseWidth || cy >= coarseHeight)
            return;

        var fx0 = Hlsl.Min(cx * 2, fineWidth - 1);
        var fx1 = Hlsl.Min(cx * 2 + 1, fineWidth - 1);
        var fy0 = Hlsl.Min(cy * 2, fineHeight - 1);
        var fy1 = Hlsl.Min(cy * 2 + 1, fineHeight - 1);

        var inside =
            (mask[fineOffset + fy0 * fineWidth + fx0] & InflateSettings.MaskInside) +
            (mask[fineOffset + fy0 * fineWidth + fx1] & InflateSettings.MaskInside) +
            (mask[fineOffset + fy1 * fineWidth + fx0] & InflateSettings.MaskInside) +
            (mask[fineOffset + fy1 * fineWidth + fx1] & InflateSettings.MaskInside);
        var kept = inside >= InflateSettings.CoarseSolidCount || (inside == InflateSettings.CoarsePairCount && ((cx + cy) & 1) == 0);
        mask[coarseOffset + cy * coarseWidth + cx] = kept ? InflateSettings.MaskInside : 0u;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MaskNeighborShader(
    ReadWriteBuffer<uint> mask,
    int offset,
    int width,
    int height) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> mask = mask;
    private readonly int offset = offset;
    private readonly int width = width;
    private readonly int height = height;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
            return;

        var index = offset + y * width + x;
        if ((mask[index] & InflateSettings.MaskInside) == 0u)
            return;

        var flags = InflateSettings.MaskInside;
        var outside = 0u;
        if (x > 0 && (mask[index - 1] & InflateSettings.MaskInside) != 0u)
            flags |= InflateSettings.MaskLeft;
        else
            outside += 1u;
        if (x < width - 1 && (mask[index + 1] & InflateSettings.MaskInside) != 0u)
            flags |= InflateSettings.MaskRight;
        else
            outside += 1u;
        if (y > 0 && (mask[index - width] & InflateSettings.MaskInside) != 0u)
            flags |= InflateSettings.MaskUp;
        else
            outside += 1u;
        if (y < height - 1 && (mask[index + width] & InflateSettings.MaskInside) != 0u)
            flags |= InflateSettings.MaskDown;
        else
            outside += 1u;

        mask[index] = flags | (outside << InflateSettings.MaskOutsideShift);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CoarseNeighborShader(
    ReadWriteBuffer<uint> mask,
    int fineOffset,
    int fineWidth,
    int fineHeight,
    int offset,
    int width,
    int height) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> mask = mask;
    private readonly int fineOffset = fineOffset;
    private readonly int fineWidth = fineWidth;
    private readonly int fineHeight = fineHeight;
    private readonly int offset = offset;
    private readonly int width = width;
    private readonly int height = height;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
            return;

        var index = offset + y * width + x;
        if ((mask[index] & InflateSettings.MaskInside) == 0u)
            return;

        var fx0 = Hlsl.Min(x * 2, fineWidth - 1);
        var fx1 = Hlsl.Min(x * 2 + 1, fineWidth - 1);
        var fy0 = Hlsl.Min(y * 2, fineHeight - 1);
        var fy1 = Hlsl.Min(y * 2 + 1, fineHeight - 1);
        var topLeft = mask[fineOffset + fy0 * fineWidth + fx0];
        var topRight = mask[fineOffset + fy0 * fineWidth + fx1];
        var bottomLeft = mask[fineOffset + fy1 * fineWidth + fx0];
        var bottomRight = mask[fineOffset + fy1 * fineWidth + fx1];

        var flags = InflateSettings.MaskInside;
        var outside = 0u;
        if (x > 0 && (mask[index - 1] & InflateSettings.MaskInside) != 0u && ((topLeft | bottomLeft) & InflateSettings.MaskLeft) != 0u)
            flags |= InflateSettings.MaskLeft;
        else
            outside += 1u;
        if (x < width - 1 && (mask[index + 1] & InflateSettings.MaskInside) != 0u && ((topRight | bottomRight) & InflateSettings.MaskRight) != 0u)
            flags |= InflateSettings.MaskRight;
        else
            outside += 1u;
        if (y > 0 && (mask[index - width] & InflateSettings.MaskInside) != 0u && ((topLeft | topRight) & InflateSettings.MaskUp) != 0u)
            flags |= InflateSettings.MaskUp;
        else
            outside += 1u;
        if (y < height - 1 && (mask[index + width] & InflateSettings.MaskInside) != 0u && ((bottomLeft | bottomRight) & InflateSettings.MaskDown) != 0u)
            flags |= InflateSettings.MaskDown;
        else
            outside += 1u;

        mask[index] = flags | (outside << InflateSettings.MaskOutsideShift);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ResidualShader(
    ReadWriteBuffer<float> potential,
    ReadWriteBuffer<uint> mask,
    ReadWriteBuffer<float> residual,
    int offset,
    int width,
    int height,
    float rhs) : IComputeShader
{
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly ReadWriteBuffer<uint> mask = mask;
    private readonly ReadWriteBuffer<float> residual = residual;
    private readonly int offset = offset;
    private readonly int width = width;
    private readonly int height = height;
    private readonly float rhs = rhs;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
            return;

        var index = offset + y * width + x;
        var flags = mask[index];
        if ((flags & InflateSettings.MaskInside) == 0u)
        {
            residual[index] = 0f;
            return;
        }

        var sum = 0f;
        if ((flags & InflateSettings.MaskLeft) != 0u)
            sum += potential[index - 1];
        if ((flags & InflateSettings.MaskRight) != 0u)
            sum += potential[index + 1];
        if ((flags & InflateSettings.MaskUp) != 0u)
            sum += potential[index - width];
        if ((flags & InflateSettings.MaskDown) != 0u)
            sum += potential[index + width];

        var outside = (float)((flags >> InflateSettings.MaskOutsideShift) & InflateSettings.MaskOutsideBits);
        residual[index] = rhs - ((4f + outside) * potential[index] - sum);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RestrictResidualShader(
    ReadWriteBuffer<float> residual,
    ReadWriteBuffer<uint> mask,
    int fineOffset,
    int fineWidth,
    int fineHeight,
    int coarseOffset,
    int coarseWidth,
    int coarseHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<float> residual = residual;
    private readonly ReadWriteBuffer<uint> mask = mask;
    private readonly int fineOffset = fineOffset;
    private readonly int fineWidth = fineWidth;
    private readonly int fineHeight = fineHeight;
    private readonly int coarseOffset = coarseOffset;
    private readonly int coarseWidth = coarseWidth;
    private readonly int coarseHeight = coarseHeight;

    public void Execute()
    {
        var cx = ThreadIds.X;
        var cy = ThreadIds.Y;
        if (cx >= coarseWidth || cy >= coarseHeight)
            return;

        var coarseIndex = coarseOffset + cy * coarseWidth + cx;
        if ((mask[coarseIndex] & InflateSettings.MaskInside) == 0u)
        {
            residual[coarseIndex] = 0f;
            return;
        }

        var fx0 = Hlsl.Min(cx * 2, fineWidth - 1);
        var fx1 = Hlsl.Min(cx * 2 + 1, fineWidth - 1);
        var fy0 = Hlsl.Min(cy * 2, fineHeight - 1);
        var fy1 = Hlsl.Min(cy * 2 + 1, fineHeight - 1);

        residual[coarseIndex] =
            residual[fineOffset + fy0 * fineWidth + fx0] +
            residual[fineOffset + fy0 * fineWidth + fx1] +
            residual[fineOffset + fy1 * fineWidth + fx0] +
            residual[fineOffset + fy1 * fineWidth + fx1];
    }
}

[ThreadGroupSize(InflateSettings.CoarseSolveThreads, 1, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CoarseSolveShader(
    ReadWriteBuffer<uint> mask,
    ReadWriteBuffer<float> residual,
    ReadWriteBuffer<float> potential,
    int offset,
    int width,
    int height,
    int iterations) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> mask = mask;
    private readonly ReadWriteBuffer<float> residual = residual;
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly int offset = offset;
    private readonly int width = width;
    private readonly int height = height;
    private readonly int iterations = iterations;

    [GroupShared(InflateSettings.CoarseSolveThreads)]
    private static readonly uint[] flagsTile = null!;

    [GroupShared(InflateSettings.CoarseSolveRegions * InflateSettings.CoarseSolveThreads)]
    private static readonly float[] shared = null!;

    public void Execute()
    {
        var thread = ThreadIds.X;
        var length = width * height;
        var rhsRegion = 0;
        var regionA = InflateSettings.CoarseSolveThreads;
        var regionB = 2 * InflateSettings.CoarseSolveThreads;

        if (thread < length)
        {
            flagsTile[thread] = mask[offset + thread];
            shared[rhsRegion + thread] = residual[offset + thread];
            shared[regionA + thread] = 0f;
            shared[regionB + thread] = 0f;
        }
        Hlsl.GroupMemoryBarrierWithGroupSync();

        for (var step = 0; step < iterations; step++)
        {
            var reading = (step & 1) == 0 ? regionA : regionB;
            var writing = (step & 1) == 0 ? regionB : regionA;
            if (thread < length)
            {
                var flags = flagsTile[thread];
                if ((flags & InflateSettings.MaskInside) == 0u)
                {
                    shared[writing + thread] = 0f;
                }
                else
                {
                    var sum = 0f;
                    if ((flags & InflateSettings.MaskLeft) != 0u)
                        sum += shared[reading + thread - 1];
                    if ((flags & InflateSettings.MaskRight) != 0u)
                        sum += shared[reading + thread + 1];
                    if ((flags & InflateSettings.MaskUp) != 0u)
                        sum += shared[reading + thread - width];
                    if ((flags & InflateSettings.MaskDown) != 0u)
                        sum += shared[reading + thread + width];
                    var outside = (float)((flags >> InflateSettings.MaskOutsideShift) & InflateSettings.MaskOutsideBits);
                    shared[writing + thread] = (shared[rhsRegion + thread] + sum) / (4f + outside);
                }
            }
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }

        if (thread < length)
            potential[offset + thread] = (iterations & 1) == 0 ? shared[regionA + thread] : shared[regionB + thread];
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ProlongShader(
    ReadWriteBuffer<float> potential,
    ReadWriteBuffer<uint> mask,
    ReadWriteBuffer<float> scale,
    int coarseOffset,
    int coarseWidth,
    int coarseHeight,
    int fineOffset,
    int fineWidth,
    int fineHeight,
    int accumulate) : IComputeShader
{
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly ReadWriteBuffer<uint> mask = mask;
    private readonly ReadWriteBuffer<float> scale = scale;
    private readonly int coarseOffset = coarseOffset;
    private readonly int coarseWidth = coarseWidth;
    private readonly int coarseHeight = coarseHeight;
    private readonly int fineOffset = fineOffset;
    private readonly int fineWidth = fineWidth;
    private readonly int fineHeight = fineHeight;
    private readonly int accumulate = accumulate;

    public void Execute()
    {
        var fx = ThreadIds.X;
        var fy = ThreadIds.Y;
        if (fx >= fineWidth || fy >= fineHeight)
            return;

        var fineIndex = fineOffset + fy * fineWidth + fx;
        if ((mask[fineIndex] & InflateSettings.MaskInside) == 0u)
        {
            if (accumulate == 0)
                potential[fineIndex] = 0f;
            return;
        }

        var px = Hlsl.Clamp((fx + 0.5f) * coarseWidth / fineWidth - 0.5f, 0f, coarseWidth - 1f);
        var py = Hlsl.Clamp((fy + 0.5f) * coarseHeight / fineHeight - 0.5f, 0f, coarseHeight - 1f);
        var ix0 = (int)px;
        var iy0 = (int)py;
        var wx = px - ix0;
        var wy = py - iy0;
        var ix1 = Hlsl.Min(ix0 + 1, coarseWidth - 1);
        var iy1 = Hlsl.Min(iy0 + 1, coarseHeight - 1);

        var index00 = coarseOffset + iy0 * coarseWidth + ix0;
        var index10 = coarseOffset + iy0 * coarseWidth + ix1;
        var index01 = coarseOffset + iy1 * coarseWidth + ix0;
        var index11 = coarseOffset + iy1 * coarseWidth + ix1;
        var weight00 = (1f - wx) * (1f - wy) * (mask[index00] & InflateSettings.MaskInside);
        var weight10 = wx * (1f - wy) * (mask[index10] & InflateSettings.MaskInside);
        var weight01 = (1f - wx) * wy * (mask[index01] & InflateSettings.MaskInside);
        var weight11 = wx * wy * (mask[index11] & InflateSettings.MaskInside);
        var total = weight00 + weight10 + weight01 + weight11;

        var value = 0f;
        if (total > 1e-6f)
            value = (weight00 * potential[index00] + weight10 * potential[index10] + weight01 * potential[index01] + weight11 * potential[index11]) / total;

        if (accumulate != 0)
            potential[fineIndex] += scale[0] * value;
        else
            potential[fineIndex] = value;
    }
}

[ThreadGroupSize(InflateSettings.CorrectionGroupSize, InflateSettings.CorrectionGroupSize, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CorrectionDotShader(
    ReadWriteBuffer<float> potential,
    ReadWriteBuffer<uint> mask,
    ReadWriteBuffer<float> residual,
    ReadWriteBuffer<float> partial,
    int coarseOffset,
    int coarseWidth,
    int coarseHeight,
    int fineWidth,
    int fineHeight,
    int groupsX) : IComputeShader
{
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly ReadWriteBuffer<uint> mask = mask;
    private readonly ReadWriteBuffer<float> residual = residual;
    private readonly ReadWriteBuffer<float> partial = partial;
    private readonly int coarseOffset = coarseOffset;
    private readonly int coarseWidth = coarseWidth;
    private readonly int coarseHeight = coarseHeight;
    private readonly int fineWidth = fineWidth;
    private readonly int fineHeight = fineHeight;
    private readonly int groupsX = groupsX;

    [GroupShared(2 * InflateSettings.CorrectionGroupThreads)]
    private static readonly float[] sums = null!;

    public void Execute()
    {
        var local = GroupIds.Index;
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        var residualDot = 0f;
        var energy = 0f;
        if (x < fineWidth && y < fineHeight)
        {
            var index = y * fineWidth + x;
            var flags = mask[index];
            if ((flags & InflateSettings.MaskInside) != 0u)
            {
                var correction = Interpolate(x, y);
                var outside = (float)((flags >> InflateSettings.MaskOutsideShift) & InflateSettings.MaskOutsideBits);
                residualDot = correction * residual[index];
                energy = 2f * outside * correction * correction;
                if ((flags & InflateSettings.MaskRight) != 0u)
                {
                    var difference = correction - Interpolate(x + 1, y);
                    energy += difference * difference;
                }
                if ((flags & InflateSettings.MaskDown) != 0u)
                {
                    var difference = correction - Interpolate(x, y + 1);
                    energy += difference * difference;
                }
            }
        }
        sums[local] = residualDot;
        sums[InflateSettings.CorrectionGroupThreads + local] = energy;
        Hlsl.GroupMemoryBarrierWithGroupSync();

        for (var stride = InflateSettings.CorrectionGroupThreads / 2; stride > 0; stride /= 2)
        {
            if (local < stride)
            {
                sums[local] += sums[local + stride];
                sums[InflateSettings.CorrectionGroupThreads + local] += sums[InflateSettings.CorrectionGroupThreads + local + stride];
            }
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }

        if (local == 0)
        {
            var group = GridIds.Y * groupsX + GridIds.X;
            partial[2 * group] = sums[0];
            partial[2 * group + 1] = sums[InflateSettings.CorrectionGroupThreads];
        }
    }

    private float Interpolate(int fx, int fy)
    {
        var px = Hlsl.Clamp((fx + 0.5f) * coarseWidth / fineWidth - 0.5f, 0f, coarseWidth - 1f);
        var py = Hlsl.Clamp((fy + 0.5f) * coarseHeight / fineHeight - 0.5f, 0f, coarseHeight - 1f);
        var ix0 = (int)px;
        var iy0 = (int)py;
        var wx = px - ix0;
        var wy = py - iy0;
        var ix1 = Hlsl.Min(ix0 + 1, coarseWidth - 1);
        var iy1 = Hlsl.Min(iy0 + 1, coarseHeight - 1);

        var index00 = coarseOffset + iy0 * coarseWidth + ix0;
        var index10 = coarseOffset + iy0 * coarseWidth + ix1;
        var index01 = coarseOffset + iy1 * coarseWidth + ix0;
        var index11 = coarseOffset + iy1 * coarseWidth + ix1;
        var weight00 = (1f - wx) * (1f - wy) * (mask[index00] & InflateSettings.MaskInside);
        var weight10 = wx * (1f - wy) * (mask[index10] & InflateSettings.MaskInside);
        var weight01 = (1f - wx) * wy * (mask[index01] & InflateSettings.MaskInside);
        var weight11 = wx * wy * (mask[index11] & InflateSettings.MaskInside);
        var total = weight00 + weight10 + weight01 + weight11;
        if (total <= 1e-6f)
            return 0f;

        return (weight00 * potential[index00] + weight10 * potential[index10] + weight01 * potential[index01] + weight11 * potential[index11]) / total;
    }
}

[ThreadGroupSize(InflateSettings.CorrectionReduceThreads, 1, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CorrectionScaleShader(
    ReadWriteBuffer<float> partial,
    ReadWriteBuffer<float> scale,
    int groupCount,
    float maximum) : IComputeShader
{
    private readonly ReadWriteBuffer<float> partial = partial;
    private readonly ReadWriteBuffer<float> scale = scale;
    private readonly int groupCount = groupCount;
    private readonly float maximum = maximum;

    [GroupShared(2 * InflateSettings.CorrectionReduceThreads)]
    private static readonly float[] sums = null!;

    public void Execute()
    {
        var thread = ThreadIds.X;
        var residualDot = 0f;
        var energy = 0f;
        for (var group = thread; group < groupCount; group += InflateSettings.CorrectionReduceThreads)
        {
            residualDot += partial[2 * group];
            energy += partial[2 * group + 1];
        }
        sums[thread] = residualDot;
        sums[InflateSettings.CorrectionReduceThreads + thread] = energy;
        Hlsl.GroupMemoryBarrierWithGroupSync();

        for (var stride = InflateSettings.CorrectionReduceThreads / 2; stride > 0; stride /= 2)
        {
            if (thread < stride)
            {
                sums[thread] += sums[thread + stride];
                sums[InflateSettings.CorrectionReduceThreads + thread] += sums[InflateSettings.CorrectionReduceThreads + thread + stride];
            }
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }

        if (thread == 0)
        {
            var total = sums[InflateSettings.CorrectionReduceThreads];
            scale[0] = total > 1e-20f ? Hlsl.Clamp(sums[0] / total, 0f, maximum) : 1f;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct GaussSeidelShader(
    ReadWriteBuffer<uint> mask,
    ReadWriteBuffer<float> residual,
    ReadWriteBuffer<float> potential,
    int offset,
    int width,
    int height,
    int color,
    int constantRhs,
    float rhsValue) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> mask = mask;
    private readonly ReadWriteBuffer<float> residual = residual;
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly int offset = offset;
    private readonly int width = width;
    private readonly int height = height;
    private readonly int color = color;
    private readonly int constantRhs = constantRhs;
    private readonly float rhsValue = rhsValue;

    public void Execute()
    {
        var y = ThreadIds.Y;
        var x = ThreadIds.X * 2 + ((y + color) & 1);
        if (x >= width || y >= height)
            return;

        var index = offset + y * width + x;
        var flags = mask[index];
        if ((flags & InflateSettings.MaskInside) == 0u)
            return;

        var sum = 0f;
        if ((flags & InflateSettings.MaskLeft) != 0u)
            sum += potential[index - 1];
        if ((flags & InflateSettings.MaskRight) != 0u)
            sum += potential[index + 1];
        if ((flags & InflateSettings.MaskUp) != 0u)
            sum += potential[index - width];
        if ((flags & InflateSettings.MaskDown) != 0u)
            sum += potential[index + width];

        var outside = (float)((flags >> InflateSettings.MaskOutsideShift) & InflateSettings.MaskOutsideBits);
        var rhs = constantRhs != 0 ? rhsValue : residual[index];
        potential[index] = (rhs + sum) / (4f + outside);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RenderShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<float> potential,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int width,
    int height,
    int gridWidth,
    int gridHeight,
    float heightScale,
    float lightX,
    float lightY,
    float lightZ,
    float ambient,
    float specular,
    float specularExponent,
    float reflection,
    float rim,
    float colorKeep) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int width = width;
    private readonly int height = height;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float heightScale = heightScale;
    private readonly float lightX = lightX;
    private readonly float lightY = lightY;
    private readonly float lightZ = lightZ;
    private readonly float ambient = ambient;
    private readonly float specular = specular;
    private readonly float specularExponent = specularExponent;
    private readonly float reflection = reflection;
    private readonly float rim = rim;
    private readonly float colorKeep = colorKeep;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
            return;

        var pixel = source[new Int2(x, y)];
        var alpha = pixel.W;
        if (alpha <= 0f)
        {
            output[new Int2(x, y)] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var gx = (x + 0.5f) * gridWidth / width - 0.5f;
        var gy = (y + 0.5f) * gridHeight / height - 0.5f;
        var center = SamplePotential(gx, gy);
        var gradientX = (SamplePotential(gx + 1f, gy) - SamplePotential(gx - 1f, gy)) * 0.5f;
        var gradientY = (SamplePotential(gx, gy + 1f) - SamplePotential(gx, gy - 1f)) * 0.5f;
        var slope = heightScale / Hlsl.Sqrt(2f * Hlsl.Max(center, 0f) + InflateSettings.PotentialEpsilon);
        var normal = Hlsl.Normalize(new Float3(-gradientX * slope, -gradientY * slope, 1f));

        var light = new Float3(lightX, lightY, lightZ);
        var facing = normal.Z;
        var lightCosine = Hlsl.Dot(normal, light);
        var diffuse = Hlsl.Saturate((lightCosine + InflateSettings.LightWrap) / (1f + InflateSettings.LightWrap));
        var halfVector = Hlsl.Normalize(light + new Float3(0f, 0f, 1f));
        var highlight = Hlsl.Pow(Hlsl.Saturate(Hlsl.Dot(normal, halfVector)), specularExponent) * specular * (lightCosine > 0f ? 1f : 0f);

        var mirrorY = 2f * facing * normal.Y;
        var skyAmount = Hlsl.SmoothStep(InflateSettings.SkyBlendStart, InflateSettings.SkyBlendEnd, 0.5f - 0.5f * mirrorY);
        var sky = new Float3(InflateSettings.SkyRed, InflateSettings.SkyGreen, InflateSettings.SkyBlue);
        var ground = new Float3(InflateSettings.GroundRed, InflateSettings.GroundGreen, InflateSettings.GroundBlue);
        var environment = Hlsl.Lerp(ground, sky, skyAmount);

        var edge = 1f - facing;
        var edgeCubed = edge * edge * edge;
        var reflectionAmount = reflection * (InflateSettings.ReflectionBase + (1f - InflateSettings.ReflectionBase) * edgeCubed);

        var straight = new Float3(pixel.X, pixel.Y, pixel.Z) / alpha;
        var white = new Float3(InflateSettings.SurfaceWhite, InflateSettings.SurfaceWhite, InflateSettings.SurfaceWhite);
        var albedo = Hlsl.Lerp(white, straight, colorKeep);
        var lit = ambient + diffuse * (1f - ambient);
        var color = albedo * lit;
        color = Hlsl.Lerp(color, environment, reflectionAmount);
        color += new Float3(highlight, highlight, highlight);
        color += Hlsl.Lerp(albedo, new Float3(1f, 1f, 1f), InflateSettings.RimWhiteMix) * (rim * edgeCubed);
        color = Hlsl.Saturate(color);

        output[new Int2(x, y)] = new Float4(color.X * alpha, color.Y * alpha, color.Z * alpha, alpha);
    }

    private float SamplePotential(float gx, float gy)
    {
        var px = Hlsl.Clamp(gx, 0f, gridWidth - 1f);
        var py = Hlsl.Clamp(gy, 0f, gridHeight - 1f);
        var ix0 = (int)px;
        var iy0 = (int)py;
        var wx = px - ix0;
        var wy = py - iy0;
        var ix1 = Hlsl.Min(ix0 + 1, gridWidth - 1);
        var iy1 = Hlsl.Min(iy0 + 1, gridHeight - 1);

        var top = Hlsl.Lerp(potential[iy0 * gridWidth + ix0], potential[iy0 * gridWidth + ix1], wx);
        var bottom = Hlsl.Lerp(potential[iy1 * gridWidth + ix0], potential[iy1 * gridWidth + ix1], wx);
        return Hlsl.Lerp(top, bottom, wy);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CopyShader(
    ReadWriteBuffer<float> source,
    ReadWriteBuffer<float> destination,
    int length) : IComputeShader
{
    private readonly ReadWriteBuffer<float> source = source;
    private readonly ReadWriteBuffer<float> destination = destination;
    private readonly int length = length;

    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= length)
            return;

        destination[index] = source[index];
    }
}

internal static class InflateShaderMath
{
    private const float ChannelMaximum = 255f;
    private const int BitsPerChannel = 8;
    private const uint GoldenRatioMultiplier = 0x9E3779B9u;
    private const uint Murmur3FirstMultiplier = 0x85EBCA6Bu;
    private const uint Murmur3SecondMultiplier = 0xC2B2AE35u;
    private const int Murmur3FirstShift = 16;
    private const int Murmur3SecondShift = 13;

    public static uint MixTexel(uint index, Float4 texel)
    {
        var packed = Quantize(texel.X)
            | (Quantize(texel.Y) << BitsPerChannel)
            | (Quantize(texel.Z) << (2 * BitsPerChannel))
            | (Quantize(texel.W) << (3 * BitsPerChannel));
        return Mix(index, packed);
    }

    public static uint MixAlpha(uint index, float alpha)
        => Mix(index, Quantize(alpha));

    private static uint Quantize(float value)
        => (uint)(value * ChannelMaximum + 0.5f);

    private static uint Mix(uint index, uint packed)
    {
        var mixed = index * GoldenRatioMultiplier ^ packed;
        mixed ^= mixed >> Murmur3FirstShift;
        mixed *= Murmur3FirstMultiplier;
        mixed ^= mixed >> Murmur3SecondShift;
        mixed *= Murmur3SecondMultiplier;
        mixed ^= mixed >> Murmur3FirstShift;
        return mixed;
    }
}
