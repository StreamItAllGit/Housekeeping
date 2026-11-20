// @module: Housekeeping.Tests.V020SandboxStage2E2ETests
// @purpose: Verifies Stage 2 4-Drive Sandbox E2E harness parity, clean settings.example.json template hygiene, and root launcher arg forwarding contracts.
// @depends-on: System, System.IO, System.Text.Json, System.Text.RegularExpressions, Xunit
// @exports: Housekeeping.Tests.V020SandboxStage2E2ETests

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Housekeeping.Tests
{
    public sealed class V020SandboxStage2E2ETests
    {
        private static string ResolveDevRoot()
        {
            string? devDir = new[]
            {
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..")),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dev")
            }.FirstOrDefault(d => Directory.Exists(d) && File.Exists(Path.Combine(d, "Housekeeping.slnx")));

            Assert.False(string.IsNullOrWhiteSpace(devDir), "Unable to locate Housekeeping\\Dev root.");
            return devDir!;
        }

        [Fact]
        public void Stage2E2EScript_SatisfiesStandard02ContractAndTwoBuildChecks()
        {
            string devRoot = ResolveDevRoot();
            string scriptPath = Path.Combine(devRoot, "scripts", "run_stage2_e2e.py");
            Assert.True(File.Exists(scriptPath), $"Missing Stage 2 E2E script: {scriptPath}");

            string body = File.ReadAllText(scriptPath);
            Assert.Contains("exebox_stream", body, StringComparison.Ordinal);
            Assert.Contains("sandbox_ctl", body, StringComparison.Ordinal);
            Assert.Contains("FOUNDATION_SANDBOX_ROOT", body, StringComparison.Ordinal);
            Assert.Contains("SANDBOX_C_DRIVE", body, StringComparison.Ordinal);
            Assert.Contains("SANDBOX_DISC_DRIVE", body, StringComparison.Ordinal);
            Assert.Contains("-p:HousekeepingTestAutomation=true", body, StringComparison.Ordinal);
            Assert.Contains("-t:Rebuild,Publish", body, StringComparison.Ordinal);
            Assert.Contains("/v1/snapshot.png", body, StringComparison.Ordinal);
            Assert.Contains("--doctor", body, StringComparison.Ordinal);
            Assert.Contains("--managed-by-suite", body, StringComparison.Ordinal);
            Assert.Contains("51999", body, StringComparison.Ordinal);
        }

        [Fact]
        public void SettingsExampleTemplate_IsValidJson_AndContainsZeroWorkstationPii()
        {
            string devRoot = ResolveDevRoot();
            string templatePath = Path.Combine(devRoot, "config", "settings.example.json");
            Assert.True(File.Exists(templatePath), $"Missing tracked template: {templatePath}");

            string raw = File.ReadAllText(templatePath);
            using var doc = JsonDocument.Parse(raw);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.Equal(4, doc.RootElement.GetProperty("ConfigVersion").GetInt32());

            Assert.DoesNotContain("devuser", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WORKER-NODE", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("discord.com/api/webhooks/", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"D:\Projects\", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\Users\", raw, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RootLauncherContract_AndReleasePipeline_PreserveSuiteArgsAndSingleSetup7()
        {
            string devRoot = ResolveDevRoot();
            string argsHelper = File.ReadAllText(Path.Combine(devRoot, "Housekeeping.Launcher", "HousekeepingLauncherArgsHelper.cs"));
            Assert.Contains("RootLauncherOptions", argsHelper, StringComparison.Ordinal);
            Assert.Contains("--doctor", argsHelper, StringComparison.Ordinal);
            Assert.Contains("--bind", argsHelper, StringComparison.Ordinal);
            Assert.Contains("--suite-id", argsHelper, StringComparison.Ordinal);
            Assert.Contains("--automation-port", argsHelper, StringComparison.Ordinal);
            Assert.Contains("--automation-token", argsHelper, StringComparison.Ordinal);

            string buildScript = File.ReadAllText(Path.Combine(devRoot, "scripts", "build_installer.ps1"));
            Assert.Contains("SingleSetup7", buildScript, StringComparison.Ordinal);
            Assert.Contains("--allow-portable-zip", buildScript, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex(@"%USERPROFILE%", RegexOptions.IgnoreCase), buildScript);
        }
    }
}
