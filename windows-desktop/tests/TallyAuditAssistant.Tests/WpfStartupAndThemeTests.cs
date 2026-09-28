using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class WpfStartupAndThemeTests
{
    [Fact]
    public void VerifyNoDataTriggerHasBindingValue()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string srcDir = "";
        
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var potentialSrc = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(potentialSrc))
            {
                srcDir = potentialSrc;
                break;
            }
            // Also check windows-desktop/src in case we are running at a different level
            var potentialDesktopSrc = Path.Combine(dir.FullName, "windows-desktop", "src");
            if (Directory.Exists(potentialDesktopSrc))
            {
                srcDir = potentialDesktopSrc;
                break;
            }
            dir = dir.Parent;
        }

        if (string.IsNullOrEmpty(srcDir))
        {
            if (Directory.Exists("windows-desktop/src"))
            {
                srcDir = "windows-desktop/src";
            }
            else if (Directory.Exists("src"))
            {
                srcDir = "src";
            }
        }

        Assert.False(string.IsNullOrEmpty(srcDir), "Could not find the 'src' directory containing XAML files.");

        var xamlFiles = Directory.GetFiles(srcDir, "*.xaml", SearchOption.AllDirectories);
        Assert.NotEmpty(xamlFiles);

        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file);
            if (!content.Contains("DataTrigger"))
                continue;

            var doc = XDocument.Parse(content);
            var dataTriggers = doc.Descendants().Where(d => d.Name.LocalName == "DataTrigger");
            foreach (var dt in dataTriggers)
            {
                var valueAttr = dt.Attribute("Value");
                if (valueAttr != null)
                {
                    var val = valueAttr.Value.Trim();
                    Assert.False(val.StartsWith("{Binding", StringComparison.OrdinalIgnoreCase),
                        $"Invalid WPF DataTrigger found in '{Path.GetFileName(file)}': Value cannot be set to a Binding! Value='{val}'");
                }
            }
        }
    }

    [Fact]
    public void VerifyApplicationIconAssetExists()
    {
        // Use the same search logic to find the root
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string rootDir = "";
        
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TallyAuditAssistant.sln")))
            {
                rootDir = dir.FullName;
                break;
            }
            dir = dir.Parent;
        }

        if (string.IsNullOrEmpty(rootDir)) rootDir = Directory.GetCurrentDirectory();

        var iconPath = Path.Combine(rootDir, "windows-desktop", "src", "TallyAuditAssistant.App", "Assets", "TallyAuditAssistant.ico");
        
        // Assert icon exists and has genuine binary content (> 1KB)
        Assert.True(File.Exists(iconPath), $"Official application icon is missing at: {iconPath}");
        var fileInfo = new FileInfo(iconPath);
        Assert.True(fileInfo.Length > 1024, $"Icon file size is too small or empty: {fileInfo.Length} bytes");

        // Verify csproj specifies ApplicationIcon correctly
        var csprojPath = Path.Combine(rootDir, "windows-desktop", "src", "TallyAuditAssistant.App", "TallyAuditAssistant.App.csproj");
        Assert.True(File.Exists(csprojPath), $"Project file not found at: {csprojPath}");
        var csprojContent = File.ReadAllText(csprojPath);
        Assert.Contains(@"<ApplicationIcon>Assets\TallyAuditAssistant.ico</ApplicationIcon>", csprojContent);
        Assert.DoesNotContain(@":\", csprojContent); // No hard-coded absolute Windows drive paths

        // Verify MainWindow.xaml has Icon specified
        var mainWindowXamlPath = Path.Combine(rootDir, "windows-desktop", "src", "TallyAuditAssistant.App", "Views", "MainWindow.xaml");
        Assert.True(File.Exists(mainWindowXamlPath), $"MainWindow.xaml not found at: {mainWindowXamlPath}");
        var mainWindowContent = File.ReadAllText(mainWindowXamlPath);
        Assert.Contains("Icon=", mainWindowContent);
        Assert.Contains("TallyAuditAssistant.ico", mainWindowContent);

        // Verify Inno Setup installer script references the icon
        var innoScriptPath = Path.Combine(rootDir, "windows-desktop", "installer", "TallyAuditAssistant.iss");
        if (File.Exists(innoScriptPath))
        {
            var innoContent = File.ReadAllText(innoScriptPath);
            Assert.Contains("TallyAuditAssistant.ico", innoContent);
        }
    }
}
