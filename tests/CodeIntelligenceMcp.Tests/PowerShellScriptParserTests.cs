using CodeIntelligenceMcp.PowerShell;
using CodeIntelligenceMcp.PowerShell.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class PowerShellScriptParserTests
{
    [Fact]
    public void Parse_SimpleFunction_ExtractsFunctionName()
    {
        string content = """
            function Get-Status {
                return "ok"
            }
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.Functions.Should().HaveCount(1);
        result.Functions[0].Name.Should().Be("Get-Status");
    }

    [Fact]
    public void Parse_FunctionWithParams_ExtractsParameters()
    {
        string content = """
            function Deploy-Service {
                param(
                    [string]$Environment,
                    [int]$Port = 8080
                )
            }
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.Functions.Should().HaveCount(1);
        PowerShellFunctionInfo func = result.Functions[0];
        func.Parameters.Should().HaveCount(2);
        func.Parameters[0].Name.Should().Be("Environment");
        func.Parameters[0].Type.Should().Be("String");
        func.Parameters[1].Name.Should().Be("Port");
        func.Parameters[1].DefaultValue.Should().NotBeNull();
    }

    [Fact]
    public void Parse_CmdletBindingFunction_DetectsCmdletBinding()
    {
        string content = """
            function Invoke-Deployment {
                [CmdletBinding()]
                param([string]$Target)
            }
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.Functions[0].HasCmdletBinding.Should().BeTrue();
    }

    [Fact]
    public void Parse_MandatoryParameter_DetectsMandatory()
    {
        string content = """
            function Set-Config {
                param(
                    [Parameter(Mandatory=$true)]
                    [string]$Name
                )
            }
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.Functions[0].Parameters[0].IsMandatory.Should().BeTrue();
    }

    [Fact]
    public void Parse_PipelineParameter_DetectsPipeline()
    {
        string content = """
            function Process-Item {
                [CmdletBinding()]
                param(
                    [Parameter(ValueFromPipeline=$true)]
                    [string]$Item
                )
            }
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.Functions[0].SupportsPipeline.Should().BeTrue();
        result.Functions[0].Parameters[0].IsFromPipeline.Should().BeTrue();
    }

    [Fact]
    public void Parse_TryCatchInFunction_DetectsErrorHandling()
    {
        string content = """
            function Invoke-Safe {
                param([string]$Command)
                try {
                    Invoke-Expression $Command
                }
                catch {
                    Write-Error $_
                }
            }
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.Functions[0].HasTryCatch.Should().BeTrue();
    }

    [Fact]
    public void Parse_ImportModule_ExtractsImport()
    {
        string content = """
            Import-Module Az.Accounts
            Import-Module Az.Resources -RequiredVersion 6.7.0
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.ImportedModules.Should().HaveCount(2);
        result.ImportedModules[0].Name.Should().Be("Az.Accounts");
        result.ImportedModules[1].Name.Should().Be("Az.Resources");
        result.ImportedModules[1].Version.Should().Be("6.7.0");
    }

    [Fact]
    public void Parse_RequiresModule_ExtractsImport()
    {
        string content = """
            #Requires -Module Az.Accounts
            #Requires -Version 5.1
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.ImportedModules.Should().HaveCount(1);
        result.ImportedModules[0].Name.Should().Be("Az.Accounts");
    }

    [Fact]
    public void Parse_ScriptVariables_ExtractsTopLevelOnly()
    {
        string content = """
            $Environment = "Production"
            $Version = "1.0.0"

            function Get-Config {
                $localVar = "ignored"
            }
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.Variables.Should().HaveCount(2);
        result.Variables.Select(v => v.Name).Should().Contain(["Environment", "Version"]);
    }

    [Fact]
    public void Parse_CmdletUsages_CountsCmdlets()
    {
        string content = """
            function Deploy-App {
                Write-Host "Starting"
                Get-ChildItem -Path $Path
                Write-Host "Done"
            }
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.CmdletUsages.Should().NotBeEmpty();
        CmdletUsage writeHost = result.CmdletUsages.First(c => c.Name == "Write-Host");
        writeHost.Count.Should().Be(2);
    }

    [Fact]
    public void Parse_MultipleFunctions_ExtractsAll()
    {
        string content = """
            function Initialize-Deployment { }
            function Deploy-Services { }
            function Rollback-Deployment { }
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.Functions.Should().HaveCount(3);
        result.Functions.Select(f => f.Name).Should().Contain(
            ["Initialize-Deployment", "Deploy-Services", "Rollback-Deployment"]);
    }

    [Fact]
    public void Parse_EmptyScript_ReturnsEmptyCollections()
    {
        PowerShellFileInfo result = PowerShellScriptParser.Parse("empty.ps1", string.Empty);

        result.Functions.Should().BeEmpty();
        result.ImportedModules.Should().BeEmpty();
        result.Variables.Should().BeEmpty();
        result.CmdletUsages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_LineNumbers_AreCorrect()
    {
        string content = """
            # comment

            function Get-Data {
                param([string]$Id)
            }
            """;

        PowerShellFileInfo result = PowerShellScriptParser.Parse("test.ps1", content);

        result.Functions[0].LineStart.Should().Be(3);
    }
}
