namespace Inflate;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/Inflate;component/Resources/Shader/{shaderName}.cso", UriKind.Absolute);
}
