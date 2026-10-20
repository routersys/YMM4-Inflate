using System.Globalization;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;

namespace Inflate.Harness;

internal static class HarnessCases
{
    public static IEnumerable<(string Name, InflateEffect Effect, IReadOnlyList<int> Frames)> All()
    {
        yield return ("default", Create(), [0]);
        yield return ("height-animated-frames", Create(effect => effect.Height.CopyFrom(Linear(20d, 200d))), [0, 100, 200, 299]);
        yield return ("light-animated-frames", Create(effect => effect.LightDirection.CopyFrom(Linear(-135d, 135d))), [0, 100, 200, 299]);
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0), [0]);
        yield return ("amount-50", Create(effect => effect.Amount.Values[0].Value = 50), [0]);
        yield return ("height-0", Create(effect => effect.Height.Values[0].Value = 0), [0]);
        yield return ("height-50", Create(effect => effect.Height.Values[0].Value = 50), [0]);
        yield return ("height-300", Create(effect => effect.Height.Values[0].Value = 300), [0]);
        yield return ("quality-balanced", Create(effect => effect.Quality = InflateQuality.Balanced), [0]);
        yield return ("quality-ultra", Create(effect => effect.Quality = InflateQuality.Ultra), [0]);
        yield return ("light-direction-90", Create(effect => effect.LightDirection.Values[0].Value = 90), [0]);
        yield return ("light-direction-minus-135", Create(effect => effect.LightDirection.Values[0].Value = -135), [0]);
        yield return ("light-elevation-0", Create(effect => effect.LightElevation.Values[0].Value = 0), [0]);
        yield return ("light-elevation-90", Create(effect => effect.LightElevation.Values[0].Value = 90), [0]);
        yield return ("ambient-0", Create(effect => effect.Ambient.Values[0].Value = 0), [0]);
        yield return ("ambient-100", Create(effect => effect.Ambient.Values[0].Value = 100), [0]);
        yield return ("specular-0", Create(effect => effect.Specular.Values[0].Value = 0), [0]);
        yield return ("specular-200", Create(effect => effect.Specular.Values[0].Value = 200), [0]);
        yield return ("sharpness-0", Create(effect => effect.Sharpness.Values[0].Value = 0), [0]);
        yield return ("sharpness-100", Create(effect => effect.Sharpness.Values[0].Value = 100), [0]);
        yield return ("reflection-0", Create(effect => effect.Reflection.Values[0].Value = 0), [0]);
        yield return ("reflection-100", Create(effect => effect.Reflection.Values[0].Value = 100), [0]);
        yield return ("rim-0", Create(effect => effect.Rim.Values[0].Value = 0), [0]);
        yield return ("rim-100", Create(effect => effect.Rim.Values[0].Value = 100), [0]);
        yield return ("color-keep-0", Create(effect => effect.ColorKeep.Values[0].Value = 0), [0]);
        yield return ("color-keep-50", Create(effect => effect.ColorKeep.Values[0].Value = 50), [0]);
    }

    public static IEnumerable<(string Name, Func<InflateEffect> Create, Action<InflateEffect> Change, int Frame)> Transitions()
    {
        yield return ("amount-100-to-0", () => Create(), effect => effect.Amount.Values[0].Value = 0, 0);
        yield return ("amount-0-to-100", () => Create(effect => effect.Amount.Values[0].Value = 0), effect => effect.Amount.Values[0].Value = 100, 0);
        yield return ("amount-100-to-50", () => Create(), effect => effect.Amount.Values[0].Value = 50, 0);
        yield return ("height-100-to-200", () => Create(), effect => effect.Height.Values[0].Value = 200, 0);
        yield return ("height-100-to-0", () => Create(), effect => effect.Height.Values[0].Value = 0, 0);
        yield return ("quality-high-to-ultra", () => Create(), effect => effect.Quality = InflateQuality.Ultra, 0);
        yield return ("quality-high-to-balanced", () => Create(), effect => effect.Quality = InflateQuality.Balanced, 0);
        yield return ("light-direction-minus-45-to-90", () => Create(), effect => effect.LightDirection.Values[0].Value = 90, 0);
        yield return ("light-elevation-50-to-10", () => Create(), effect => effect.LightElevation.Values[0].Value = 10, 0);
        yield return ("ambient-30-to-0", () => Create(), effect => effect.Ambient.Values[0].Value = 0, 0);
        yield return ("specular-80-to-0", () => Create(), effect => effect.Specular.Values[0].Value = 0, 0);
        yield return ("sharpness-50-to-100", () => Create(), effect => effect.Sharpness.Values[0].Value = 100, 0);
        yield return ("reflection-30-to-100", () => Create(), effect => effect.Reflection.Values[0].Value = 100, 0);
        yield return ("rim-20-to-100", () => Create(), effect => effect.Rim.Values[0].Value = 100, 0);
        yield return ("color-keep-100-to-0", () => Create(), effect => effect.ColorKeep.Values[0].Value = 0, 0);
    }

    public static IEnumerable<(string Name, InflateEffect Effect)> Benchmarks()
    {
        yield return ("quality-balanced", Create(effect => effect.Quality = InflateQuality.Balanced));
        yield return ("default", Create());
        yield return ("quality-ultra", Create(effect => effect.Quality = InflateQuality.Ultra));
        yield return ("height-animated", Create(effect => effect.Height.CopyFrom(Linear(20d, 200d))));
        yield return ("light-animated", Create(effect => effect.LightDirection.CopyFrom(Linear(-135d, 135d))));
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0));
    }

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}""")) ?? throw new HarnessException("アニメーションを読み込めません。");

    public static InflateEffect Create(Action<InflateEffect>? configure = null)
    {
        var effect = new InflateEffect();
        configure?.Invoke(effect);
        return effect;
    }
}
