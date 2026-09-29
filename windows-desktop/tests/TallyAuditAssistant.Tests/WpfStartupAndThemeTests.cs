using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.App.Services;
using TallyAuditAssistant.App.ViewModels;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Services;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Services;
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
        var appDir = FindAppDirectory();

        var iconPath = Path.Combine(appDir, "Assets", "TallyAuditAssistant.ico");
        
        // Assert icon exists and has genuine binary content (> 1KB)
        Assert.True(File.Exists(iconPath), $"Official application icon is missing at: {iconPath}");
        var fileInfo = new FileInfo(iconPath);
        Assert.True(fileInfo.Length > 1024, $"Icon file size is too small or empty: {fileInfo.Length} bytes");

        // Validate ICO binary structure (reserved=0, type=1, count >= 1)
        using (var stream = File.OpenRead(iconPath))
        using (var reader = new BinaryReader(stream))
        {
            var reserved = reader.ReadUInt16();
            var type = reader.ReadUInt16();
            var count = reader.ReadUInt16();

            Assert.Equal(0, reserved);
            Assert.Equal(1, type); // 1 = ICO format
            Assert.True(count >= 1, "Icon file must contain at least 1 image frame.");

            for (int i = 0; i < count; i++)
            {
                var width = reader.ReadByte();
                var height = reader.ReadByte();
                var colorCount = reader.ReadByte();
                var res = reader.ReadByte();
                var planes = reader.ReadUInt16();
                var bpp = reader.ReadUInt16();
                var bytesInRes = reader.ReadUInt32();
                var imageOffset = reader.ReadUInt32();

                Assert.True(bytesInRes > 0, $"Icon frame {i} has invalid data size.");
                Assert.True(imageOffset + bytesInRes <= fileInfo.Length, $"Icon frame {i} extends beyond file bounds.");
            }
        }

        // Verify csproj specifies ApplicationIcon correctly
        var csprojPath = Path.Combine(appDir, "TallyAuditAssistant.App.csproj");
        Assert.True(File.Exists(csprojPath), $"Project file not found at: {csprojPath}");
        var csprojContent = File.ReadAllText(csprojPath);
        Assert.Contains(@"<ApplicationIcon>Assets\TallyAuditAssistant.ico</ApplicationIcon>", csprojContent);
        Assert.DoesNotContain(@":\", csprojContent); // No hard-coded absolute Windows drive paths

        // Verify csproj includes the icon as a WPF Resource and NOT as duplicate Content
        Assert.Contains(@"<Resource Include=""Assets\TallyAuditAssistant.ico"" />", csprojContent);
        Assert.DoesNotContain(@"<Content Include=""Assets\TallyAuditAssistant.ico""", csprojContent);

        // Verify Inno Setup installer script references the icon
        var windowsDesktopDir = Path.GetDirectoryName(Path.GetDirectoryName(appDir));
        if (!string.IsNullOrEmpty(windowsDesktopDir))
        {
            var innoScriptPath = Path.Combine(windowsDesktopDir, "installer", "TallyAuditAssistant.iss");
            if (File.Exists(innoScriptPath))
            {
                var innoContent = File.ReadAllText(innoScriptPath);
                Assert.Contains("TallyAuditAssistant.ico", innoContent);
            }
        }
    }

    [Fact]
    public void VerifyMainWindowXamlIconResourceUriAndStructure()
    {
        var appDir = FindAppDirectory();
        var mainWindowXamlPath = Path.Combine(appDir, "Views", "MainWindow.xaml");
        Assert.True(File.Exists(mainWindowXamlPath), $"MainWindow.xaml not found at: {mainWindowXamlPath}");

        var content = File.ReadAllText(mainWindowXamlPath);
        var doc = XDocument.Parse(content);
        var root = doc.Root;
        Assert.NotNull(root);

        // Verify Icon attribute uses a valid WPF Pack URI
        var iconAttr = root.Attribute("Icon");
        Assert.NotNull(iconAttr);
        var iconVal = iconAttr.Value.Trim();

        // Must start with pack:// to avoid TypeConverterMarkupExtension crash in self-contained deployment
        Assert.StartsWith("pack://application:,,,/", iconVal, StringComparison.OrdinalIgnoreCase);
        Assert.False(iconVal.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase), 
            "Icon cannot be a relative slash path like '/Assets/...', it must use pack://application:,,,/ to prevent TypeConverterMarkupExtension runtime crashes.");
        Assert.False(iconVal.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase),
            "Icon cannot be a filesystem path.");

        // Extract the resource path from the pack URI
        var resourcePath = iconVal.Replace("pack://application:,,,/", "").TrimStart('/');
        var physicalPath = Path.Combine(appDir, resourcePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(physicalPath), $"The resource targeted by pack URI does not exist physically at: {physicalPath}");
    }

    [Fact]
    public void VerifyAppXamlCsRegistersAllRequiredAuditAndInvestigationDependencies()
    {
        var appDir = FindAppDirectory();
        var appXamlCsPath = Path.Combine(appDir, "App.xaml.cs");
        Assert.True(File.Exists(appXamlCsPath), $"App.xaml.cs not found at: {appXamlCsPath}");

        var content = File.ReadAllText(appXamlCsPath);

        // Core & Investigation registrations
        Assert.Contains("services.AddSingleton<IAuditTrailRepository, AuditTrailRepository>()", content);
        Assert.Contains("services.AddSingleton<IAuditTrailService, AuditTrailService>()", content);
        Assert.Contains("services.AddSingleton<IInvestigationRepository, InvestigationRepository>()", content);
        Assert.Contains("services.AddSingleton<IInvestigationService, InvestigationService>()", content);
        Assert.Contains("services.AddSingleton<InvestigationViewModel>()", content);

        // Finalization & QC registrations
        Assert.Contains("services.AddSingleton<IAuditFinalizationRepository, AuditFinalizationRepository>()", content);
        Assert.Contains("services.AddSingleton<IAuditFinalizationService, AuditFinalizationService>()", content);
        Assert.Contains("services.AddSingleton<IAuditQualityControlService, AuditQualityControlService>()", content);

        // Repository registrations
        Assert.Contains("services.AddSingleton<IAuditRepository, AuditRepository>()", content);
        Assert.Contains("services.AddSingleton<ISyncRepository, SyncRepository>()", content);
        Assert.Contains("services.AddSingleton<ISettingsService, SettingsRepository>()", content);

        // Strictly Read-Only policy registration
        Assert.Contains("services.AddSingleton<ITallyReadOnlyPolicy, TallyReadOnlyPolicy>()", content);
        Assert.DoesNotContain("ITallyWriteService", content);
        Assert.DoesNotContain("ITallyCorrectionRepository", content);
    }

    [Fact]
    public void VerifyInvestigationServiceDependencyChain_CanBeActivatedByServiceContainer()
    {
        var services = new ServiceCollection();
        var tempDb = Path.Combine(Path.GetTempPath(), $"di_test_{Guid.NewGuid():N}.db");
        services.AddSingleton(new SqliteConnectionFactory(tempDb));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IAuditRepository, AuditRepository>();
        services.AddSingleton<IAuditFinalizationRepository, AuditFinalizationRepository>();
        services.AddSingleton<IAuditTrailRepository, AuditTrailRepository>();
        services.AddSingleton<IAuditTrailService, AuditTrailService>();
        services.AddSingleton<IInvestigationRepository, InvestigationRepository>();
        services.AddSingleton<IInvestigationService, InvestigationService>();

        using var provider = services.BuildServiceProvider();
        var investigationService = provider.GetRequiredService<IInvestigationService>();
        Assert.NotNull(investigationService);
        Assert.IsType<InvestigationService>(investigationService);

        var auditTrailService = provider.GetRequiredService<IAuditTrailService>();
        Assert.NotNull(auditTrailService);
        Assert.IsType<AuditTrailService>(auditTrailService);
    }

    [Fact]
    public void VerifyGstAuditViewModel_CanBeActivatedByServiceContainer()
    {
        var services = new ServiceCollection();
        var tempDb = Path.Combine(Path.GetTempPath(), $"di_gst_test_{Guid.NewGuid():N}.db");
        services.AddSingleton(new SqliteConnectionFactory(tempDb));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IAuditRepository, AuditRepository>();
        services.AddSingleton<ISettingsService, SettingsRepository>();
        services.AddSingleton<IActiveCompanyContext, ActiveCompanyContext>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<GstAuditViewModel>();

        using var provider = services.BuildServiceProvider();
        var gstViewModel = provider.GetRequiredService<GstAuditViewModel>();
        Assert.NotNull(gstViewModel);
        Assert.IsType<GstAuditViewModel>(gstViewModel);
    }

    private static string FindAppDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var directApp = Path.Combine(dir.FullName, "src", "TallyAuditAssistant.App");
            if (Directory.Exists(directApp))
            {
                return directApp;
            }

            var desktopApp = Path.Combine(dir.FullName, "windows-desktop", "src", "TallyAuditAssistant.App");
            if (Directory.Exists(desktopApp))
            {
                return desktopApp;
            }

            dir = dir.Parent;
        }

        // Fallback relative paths
        if (Directory.Exists("windows-desktop/src/TallyAuditAssistant.App"))
            return Path.GetFullPath("windows-desktop/src/TallyAuditAssistant.App");
        if (Directory.Exists("src/TallyAuditAssistant.App"))
            return Path.GetFullPath("src/TallyAuditAssistant.App");

        return Path.GetFullPath("windows-desktop/src/TallyAuditAssistant.App");
    }
}
