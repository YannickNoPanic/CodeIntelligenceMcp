using CodeIntelligenceMcp.PowerShell;
using CodeIntelligenceMcp.PowerShell.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class PowerShellManifestParserTests
{
    [Fact]
    public void Parse_BasicManifest_ExtractsName()
    {
        string content = """
            @{
                ModuleVersion = '1.2.3'
                Description = 'My module'
            }
            """;

        PowerShellModuleManifest? result = PowerShellManifestParser.Parse("MyModule.psd1", content);

        result.Should().NotBeNull();
        result!.Name.Should().Be("MyModule");
        result.Version.Should().Be("1.2.3");
        result.Description.Should().Be("My module");
    }

    [Fact]
    public void Parse_ExportedFunctions_ExtractsList()
    {
        string content = """
            @{
                ModuleVersion = '1.0.0'
                FunctionsToExport = @('Get-Status', 'Set-Config', 'Invoke-Deploy')
            }
            """;

        PowerShellModuleManifest? result = PowerShellManifestParser.Parse("Deploy.psd1", content);

        result!.ExportedFunctions.Should().HaveCount(3);
        result.ExportedFunctions.Should().Contain(["Get-Status", "Set-Config", "Invoke-Deploy"]);
    }

    [Fact]
    public void Parse_RequiredModules_ExtractsList()
    {
        string content = """
            @{
                ModuleVersion = '2.0.0'
                RequiredModules = @('Az.Accounts', 'Az.Resources')
            }
            """;

        PowerShellModuleManifest? result = PowerShellManifestParser.Parse("AzDeploy.psd1", content);

        result!.RequiredModules.Should().HaveCount(2);
        result.RequiredModules.Should().Contain(["Az.Accounts", "Az.Resources"]);
    }

    [Fact]
    public void Parse_RequiredModulesHashtable_ExtractsModuleNames()
    {
        string content = """
            @{
                ModuleVersion = '1.0.0'
                RequiredModules = @(
                    @{ ModuleName = 'Az.Accounts'; ModuleVersion = '2.12.0' },
                    'SimpleModule'
                )
            }
            """;

        PowerShellModuleManifest? result = PowerShellManifestParser.Parse("Complex.psd1", content);

        result!.RequiredModules.Should().Contain("Az.Accounts");
        result.RequiredModules.Should().Contain("SimpleModule");
    }

    [Fact]
    public void Parse_WildcardExports_ReturnsEmpty()
    {
        string content = """
            @{
                ModuleVersion = '1.0.0'
                FunctionsToExport = '*'
            }
            """;

        PowerShellModuleManifest? result = PowerShellManifestParser.Parse("Wild.psd1", content);

        result!.ExportedFunctions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ManifestPath_IsPreserved()
    {
        string content = """
            @{
                ModuleVersion = '1.0.0'
            }
            """;

        PowerShellModuleManifest? result = PowerShellManifestParser.Parse(
            "C:/Scripts/MyModule/MyModule.psd1", content);

        result!.ManifestPath.Should().Be("C:/Scripts/MyModule/MyModule.psd1");
    }
}
