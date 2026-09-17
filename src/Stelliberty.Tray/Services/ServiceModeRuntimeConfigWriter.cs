using System.Text;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Tray;

public static class ServiceModeRuntimeConfigWriter
{
    public static string Write(string content, string corePipe)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(content);
        stream.Load(reader);
        var root = stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode mapping
            ? mapping
            : new YamlMappingNode();

        var controllerKey = OperatingSystem.IsWindows() ? "external-controller-pipe" : "external-controller-unix";
        var staleControllerKey = OperatingSystem.IsWindows() ? "external-controller-unix" : "external-controller-pipe";
        Set(root, controllerKey, corePipe);
        Remove(root, staleControllerKey);

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);
        return output.ToString();
    }

    private static void Set(YamlMappingNode mapping, string key, string value)
    {
        mapping.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);
    }

    private static void Remove(YamlMappingNode mapping, string key)
    {
        mapping.Children.Remove(new YamlScalarNode(key));
    }
}
