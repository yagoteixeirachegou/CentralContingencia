using System.Xml.Linq;
namespace CentralContingencia.UnitTests;
public class ArchitectureTests
{
    [Fact]
    public void LayersRespectAllowedDependencies()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CentralContingencia.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var allowed = new Dictionary<string, string[]>
        {
            ["Domain"] = [],
            ["Application"] = ["Domain"],
            ["Infrastructure"] = ["Application", "Domain"],
            ["Api"] = ["Application", "Infrastructure"]
        };
        foreach (var (layer, dependencies) in allowed)
        {
            var project = XDocument.Load(Path.Combine(root.FullName, "src", $"CentralContingencia.{layer}", $"CentralContingencia.{layer}.csproj"));
            var references = project.Descendants("ProjectReference").Select(r =>
                Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')).Replace("CentralContingencia.", ""));
            Assert.Equal(dependencies.Order(), references.Order());
            if (layer is "Domain" or "Application") Assert.Empty(project.Descendants("PackageReference"));
        }
    }
}

