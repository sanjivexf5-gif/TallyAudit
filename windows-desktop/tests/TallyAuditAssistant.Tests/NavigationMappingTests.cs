using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TallyAuditAssistant.Core.Interfaces;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class NavigationMappingTests
{
    private static readonly Dictionary<string, (string ViewModelName, string ViewName)> ExpectedNavigationMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = ("DashboardViewModel", "DashboardView"),
            ["TallyConnection"] = ("TallyConnectionViewModel", "TallyConnectionView"),
            ["Companies"] = ("CompaniesViewModel", "CompaniesView"),
            ["Sync"] = ("SyncViewModel", "SyncView"),
            ["GST"] = ("GstAuditViewModel", "GstAuditView"),
            ["TDS"] = ("TdsAuditViewModel", "TdsAuditView"),
            ["Vouchers"] = ("VouchersViewModel", "VouchersView"),
            ["Ledgers"] = ("LedgersViewModel", "LedgersView"),
            ["Bank"] = ("BankAuditViewModel", "BankAuditView"),
            ["Exceptions"] = ("ExceptionsViewModel", "ExceptionsView"),
            ["Reports"] = ("ReportsViewModel", "ReportsView"),
            ["WorkingPapers"] = ("WorkingPapersViewModel", "WorkingPapersView"),
            ["AuditChecklist"] = ("AuditChecklistViewModel", "AuditChecklistView"),
            ["Settings"] = ("SettingsViewModel", "SettingsView"),
            ["AuditTrail"] = ("AuditTrailViewModel", "AuditTrailView"),
            ["ManagementLetter"] = ("ManagementRepresentationLetterViewModel", "ManagementRepresentationLetterView"),
        };

    [Fact]
    public void VerifyMainWindowXamlSidebarContainsAllSixteenNavigationRoutes()
    {
        var appDir = FindAppDirectory();
        var mainWindowXamlPath = Path.Combine(appDir, "Views", "MainWindow.xaml");
        Assert.True(File.Exists(mainWindowXamlPath), $"MainWindow.xaml not found at: {mainWindowXamlPath}");

        var doc = XDocument.Parse(File.ReadAllText(mainWindowXamlPath));
        var buttons = doc.Descendants()
            .Where(e => e.Name.LocalName == "Button" &&
                        e.Attribute("CommandParameter") != null &&
                        (e.Attribute("Command")?.Value.Contains("NavigateCommand") ?? false))
            .ToList();

        Assert.Equal(16, buttons.Count);

        var registeredSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var btn in buttons)
        {
            var param = btn.Attribute("CommandParameter")?.Value.Trim();
            var tag = btn.Attribute("Tag")?.Value.Trim();

            Assert.False(string.IsNullOrEmpty(param), "Navigation button has empty CommandParameter.");
            Assert.Equal(param, tag); // Ensure Tag matches CommandParameter for active selection highlight

            Assert.True(ExpectedNavigationMappings.ContainsKey(param), 
                $"MainWindow.xaml contains unexpected navigation button section: '{param}'");

            registeredSections.Add(param);
        }

        foreach (var expectedSection in ExpectedNavigationMappings.Keys)
        {
            Assert.Contains(expectedSection, registeredSections);
        }
    }

    [Fact]
    public void VerifyMainWindowXamlDataTemplatesMapAllSixteenViewModelsToCorrectViews()
    {
        var appDir = FindAppDirectory();
        var mainWindowXamlPath = Path.Combine(appDir, "Views", "MainWindow.xaml");
        var doc = XDocument.Parse(File.ReadAllText(mainWindowXamlPath));

        var dataTemplates = doc.Descendants()
            .Where(e => e.Name.LocalName == "DataTemplate" && e.Attribute("DataType") != null)
            .ToList();

        Assert.True(dataTemplates.Count >= 16, $"Expected at least 15 DataTemplates, found {dataTemplates.Count}");

        foreach (var kvp in ExpectedNavigationMappings)
        {
            var section = kvp.Key;
            var (expectedVm, expectedView) = kvp.Value;

            var matchingTemplate = dataTemplates.FirstOrDefault(dt =>
            {
                var dtAttr = dt.Attribute("DataType")?.Value ?? "";
                return dtAttr.Contains(expectedVm);
            });

            Assert.NotNull(matchingTemplate);

            var childView = matchingTemplate.Elements().FirstOrDefault();
            Assert.NotNull(childView);
            Assert.Equal(expectedView, childView.Name.LocalName);
        }
    }

    [Fact]
    public void VerifyInvestigationViewModelHasCorrectDataTemplateInMainWindowXaml()
    {
        var appDir = FindAppDirectory();
        var mainWindowXamlPath = Path.Combine(appDir, "Views", "MainWindow.xaml");
        var doc = XDocument.Parse(File.ReadAllText(mainWindowXamlPath));

        var dataTemplates = doc.Descendants()
            .Where(e => e.Name.LocalName == "DataTemplate" && e.Attribute("DataType") != null)
            .ToList();

        var matchingTemplate = dataTemplates.FirstOrDefault(dt =>
        {
            var dtAttr = dt.Attribute("DataType")?.Value ?? "";
            return dtAttr.Contains("InvestigationViewModel");
        });

        Assert.NotNull(matchingTemplate);

        var childView = matchingTemplate.Elements().FirstOrDefault();
        Assert.NotNull(childView);
        Assert.Equal("InvestigationView", childView.Name.LocalName);
    }

    [Fact]
    public void VerifyMainWindowXamlHasExplicitKeyedTemplateAndDataTriggerForInvestigation()
    {
        var appDir = FindAppDirectory();
        var mainWindowXamlPath = Path.Combine(appDir, "Views", "MainWindow.xaml");
        var doc = XDocument.Parse(File.ReadAllText(mainWindowXamlPath));

        // 1. Verify the keyed data template is declared
        var keyedTemplate = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "DataTemplate" && 
                                 e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "InvestigationViewTemplate");
        Assert.NotNull(keyedTemplate);

        var innerView = keyedTemplate.Elements().FirstOrDefault();
        Assert.NotNull(innerView);
        Assert.Equal("InvestigationView", innerView.Name.LocalName);

        // 2. Verify ContentControl has a Style with a DataTrigger matching Investigation
        var contentControl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ContentControl");
        Assert.NotNull(contentControl);

        var dataTrigger = contentControl.Descendants().FirstOrDefault(e => e.Name.LocalName == "DataTrigger");
        Assert.NotNull(dataTrigger);

        var bindingAttr = dataTrigger.Attribute("Binding")?.Value ?? "";
        var valueAttr = dataTrigger.Attribute("Value")?.Value ?? "";
        Assert.Contains("CurrentSection", bindingAttr);
        Assert.Equal("Investigation", valueAttr);

        var setter = dataTrigger.Descendants().FirstOrDefault(e => e.Name.LocalName == "Setter");
        Assert.NotNull(setter);
        Assert.Equal("ContentTemplate", setter.Attribute("Property")?.Value);
        Assert.Contains("InvestigationViewTemplate", setter.Attribute("Value")?.Value ?? "");
    }

    [Fact]
    public void VerifyMainWindowViewModelSourceCodeContainsCompleteSixteenSectionRouting()
    {
        var appDir = FindAppDirectory();
        var vmPath = Path.Combine(appDir, "ViewModels", "MainWindowViewModel.cs");
        Assert.True(File.Exists(vmPath), $"MainWindowViewModel.cs not found at: {vmPath}");

        var content = File.ReadAllText(vmPath);

        foreach (var kvp in ExpectedNavigationMappings)
        {
            var section = kvp.Key;
            Assert.Contains($"\"{section}\"", content);
        }

        // Verify INavigationService is injected and handled
        Assert.Contains("INavigationService", content);
        Assert.Contains("Navigate(string section)", content);
        Assert.Contains("INavigationAware", content);
    }

    [Fact]
    public void VerifyAllSixteenViewAndViewModelFilesExistPhysicallyOnDisk()
    {
        var appDir = FindAppDirectory();

        foreach (var kvp in ExpectedNavigationMappings)
        {
            var (vmName, viewName) = kvp.Value;

            var vmFile = Path.Combine(appDir, "ViewModels", $"{vmName}.cs");
            Assert.True(File.Exists(vmFile), $"ViewModel file missing: {vmFile}");

            var viewXamlFile = Path.Combine(appDir, "Views", $"{viewName}.xaml");
            Assert.True(File.Exists(viewXamlFile), $"View XAML file missing: {viewXamlFile}");

            var viewCsFile = Path.Combine(appDir, "Views", $"{viewName}.xaml.cs");
            Assert.True(File.Exists(viewCsFile), $"View code-behind file missing: {viewCsFile}");
        }
    }

    [Fact]
    public void VerifyNoBlankScreensAndActionableButtonsInViews()
    {
        var appDir = FindAppDirectory();

        // 1. VouchersView: Must have Loading, DataGrid, and Empty state with GoToSync and LoadVouchers commands
        var vouchersXaml = File.ReadAllText(Path.Combine(appDir, "Views", "VouchersView.xaml"));
        Assert.Contains("GoToSyncCommand", vouchersXaml);
        Assert.Contains("LoadVouchersCommand", vouchersXaml);
        Assert.Contains("IsLoading", vouchersXaml);
        Assert.Contains("No synchronized voucher data available", vouchersXaml);

        // 2. LedgersView: Must have Loading, DataGrid, and Empty state with GoToSync and LoadLedgers commands
        var ledgersXaml = File.ReadAllText(Path.Combine(appDir, "Views", "LedgersView.xaml"));
        Assert.Contains("GoToSyncCommand", ledgersXaml);
        Assert.Contains("LoadLedgersCommand", ledgersXaml);
        Assert.Contains("IsLoading", ledgersXaml);
        Assert.Contains("No synchronized ledger data available", ledgersXaml);

        // 3. GstAuditView: Must have RunComprehensiveAuditCommand and IsLoading
        var gstXaml = File.ReadAllText(Path.Combine(appDir, "Views", "GstAuditView.xaml"));
        Assert.Contains("RunComprehensiveAuditCommand", gstXaml);
        Assert.Contains("LoadGstExceptionsCommand", gstXaml);
        Assert.Contains("IsLoading", gstXaml);

        // 4. TdsAuditView: Must have RunComprehensiveAuditCommand and IsLoading
        var tdsXaml = File.ReadAllText(Path.Combine(appDir, "Views", "TdsAuditView.xaml"));
        Assert.Contains("RunComprehensiveAuditCommand", tdsXaml);
        Assert.Contains("LoadTdsExceptionsCommand", tdsXaml);
        Assert.Contains("IsLoading", tdsXaml);

        // 5. BankAuditView: Must have RunComprehensiveAuditCommand and IsLoading
        var bankXaml = File.ReadAllText(Path.Combine(appDir, "Views", "BankAuditView.xaml"));
        Assert.Contains("RunComprehensiveAuditCommand", bankXaml);
        Assert.Contains("LoadBankExceptionsCommand", bankXaml);
        Assert.Contains("IsLoading", bankXaml);

        // 6. ExceptionsView: Must have RunComprehensiveAuditCommand, LoadExceptionsCommand, and IsLoading
        var exXaml = File.ReadAllText(Path.Combine(appDir, "Views", "ExceptionsView.xaml"));
        Assert.Contains("RunComprehensiveAuditCommand", exXaml);
        Assert.Contains("LoadExceptionsCommand", exXaml);
        Assert.Contains("IsLoading", exXaml);

        // 7. ReportsView: Must have export commands, open file/folder commands, and StatusMessage
        var reportsXaml = File.ReadAllText(Path.Combine(appDir, "Views", "ReportsView.xaml"));
        Assert.Contains("GenerateExcelReportCommand", reportsXaml);
        Assert.Contains("GeneratePdfReportCommand", reportsXaml);
        Assert.Contains("GenerateCsvReportCommand", reportsXaml);
        Assert.Contains("OpenFileCommand", reportsXaml);
        Assert.Contains("OpenFolderCommand", reportsXaml);
        Assert.Contains("StatusMessage", reportsXaml);
        Assert.Contains("IsLoading", reportsXaml);
    }

    private static string FindAppDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var directApp = Path.Combine(dir.FullName, "src", "TallyAuditAssistant.App");
            if (Directory.Exists(directApp)) return directApp;

            var desktopApp = Path.Combine(dir.FullName, "windows-desktop", "src", "TallyAuditAssistant.App");
            if (Directory.Exists(desktopApp)) return desktopApp;

            dir = dir.Parent;
        }

        if (Directory.Exists("windows-desktop/src/TallyAuditAssistant.App"))
            return Path.GetFullPath("windows-desktop/src/TallyAuditAssistant.App");
        if (Directory.Exists("src/TallyAuditAssistant.App"))
            return Path.GetFullPath("src/TallyAuditAssistant.App");

        return Path.GetFullPath("windows-desktop/src/TallyAuditAssistant.App");
    }
}
