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
}
