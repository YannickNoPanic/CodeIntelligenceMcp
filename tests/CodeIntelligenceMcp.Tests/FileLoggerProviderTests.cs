using CodeIntelligenceMcp.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("citest-log").FullName;
    private string LogPath => Path.Combine(_dir, "t.log");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Ctor_ExistingFileOverSizeCap_RollsToOld()
    {
        File.WriteAllBytes(LogPath, new byte[11 * 1024 * 1024]);

        using (new FileLoggerProvider(LogPath)) { }

        File.Exists(LogPath + ".old").Should().BeTrue();
        new FileInfo(LogPath).Length.Should().BeLessThan(1024);
    }

    [Fact]
    public void Ctor_SmallExistingFile_DoesNotRoll()
    {
        File.WriteAllText(LogPath, "previous session");

        using (new FileLoggerProvider(LogPath)) { }

        File.Exists(LogPath + ".old").Should().BeFalse();
        File.ReadAllText(LogPath).Should().StartWith("previous session");
    }

    [Fact]
    public void Log_WithException_WritesStackTrace()
    {
        Exception caught;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        using (var provider = new FileLoggerProvider(LogPath))
            provider.CreateLogger("Test").LogError(caught, "failed");

        string log = File.ReadAllText(LogPath);
        log.Should().Contain("boom").And.Contain("   at ");
    }
}
