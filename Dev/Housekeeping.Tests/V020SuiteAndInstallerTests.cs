// @module: Housekeeping.Tests.V020SuiteAndInstallerTests
// @purpose: Covers StandardInstallerHost v0.2.0 CLI/profiles/--suite-dir/--payload negative checks, Mother --bind/--managed-by-suite, subpath proxy headers, and SVG QR endpoints.
// @depends-on: System, System.IO, System.IO.Compression, System.Linq, System.Net, System.Net.Http, System.Net.Sockets, System.Text.Json, System.Threading.Tasks, Housekeeping.Installer, Housekeeping.Server.Hosting, Housekeeping.Services, Xunit
// @exports: Housekeeping.Tests.V020SuiteAndInstallerTests

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Housekeeping.Installer;
using Housekeeping.Server.Hosting;
using Xunit;

namespace Housekeeping.Tests
{
    public sealed class V020SuiteAndInstallerTests
    {
        private static int GetFreeLoopbackPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static string CreateValidTestPayloadZip(string root)
        {
            string zipPath = Path.Combine(root, "valid-payload.zip");
            using var fs = File.Create(zipPath);
            using var za = new ZipArchive(fs, ZipArchiveMode.Create);
            WriteZipEntry(za, "Housekeeping.exe", "launcher-stub");
            WriteZipEntry(za, "app/Housekeeping.exe", "app-stub");
            WriteZipEntry(za, "app/version.txt", "0.2.0");
            return zipPath;
        }

        private static void WriteZipEntry(ZipArchive za, string entryName, string content)
        {
            ZipArchiveEntry e = za.CreateEntry(entryName);
            using var writer = new StreamWriter(e.Open());
            writer.Write(content);
        }

        [Fact]
        public void StandardInstallerHost_VerifySilentProfiles_AndPayloadZipSlipCorruptFailClosed()
        {
            string root = Path.Combine(Path.GetTempPath(), "hk_inst_v020_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string? prevIso = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
            Environment.SetEnvironmentVariable("APP_ISOLATED_TEST", "1");
            try
            {
                var asm = typeof(HousekeepingInstallerRunner).Assembly;
                Assert.Equal(0, HousekeepingInstallerRunner.Run(["--version"], "0.2.0", asm));

                string validZip = CreateValidTestPayloadZip(root);
                Assert.Equal(0, HousekeepingInstallerRunner.Run(["--verify", "--payload", validZip], "0.2.0", asm));

                string suiteDir = Path.Combine(root, "Mother");
                string installFull = Path.Combine(suiteDir, "Housekeeping");
                string dataFull = Path.Combine(root, "data_full");
                string logsFull = Path.Combine(root, "logs_full");
                int rcFull = HousekeepingInstallerRunner.Run(
                    ["--silent", "--no-shortcuts", "--no-launch", "--profile", "full", "--suite-dir", suiteDir, "--install-dir", installFull, "--data-dir", dataFull, "--log-dir", logsFull, "--payload", validZip],
                    "0.2.0",
                    asm);
                Assert.Equal(0, rcFull);
                Assert.True(File.Exists(Path.Combine(installFull, "Housekeeping.exe")));
                Assert.Empty(Directory.GetFiles(installFull, "AGENTS.md", SearchOption.AllDirectories));

                string installLight = Path.Combine(root, "light");
                int rcLight = HousekeepingInstallerRunner.Run(
                    ["--silent", "--no-shortcuts", "--no-launch", "--profile", "lightweight", "--install-dir", installLight, "--data-dir", Path.Combine(root, "data_light"), "--log-dir", Path.Combine(root, "logs_light"), "--payload", validZip],
                    "0.2.0",
                    asm);
                Assert.Equal(0, rcLight);

                string escapeMarker = "hk_zipslip_escape_" + Guid.NewGuid().ToString("N") + ".txt";
                string zipSlipZip = Path.Combine(root, "zipslip-payload.zip");
                using (var fs = File.Create(zipSlipZip))
                using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    WriteZipEntry(za, $"../../{escapeMarker}", "pwned");
                }
                string installSlip = Path.Combine(root, "nest", "slip");
                int rcSlip = HousekeepingInstallerRunner.Run(
                    ["--silent", "--no-shortcuts", "--no-launch", "--install-dir", installSlip, "--data-dir", Path.Combine(root, "data_slip"), "--log-dir", Path.Combine(root, "logs_slip"), "--payload", zipSlipZip],
                    "0.2.0",
                    asm);
                Assert.NotEqual(0, rcSlip);
                Assert.False(File.Exists(Path.Combine(root, escapeMarker)));
                Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), escapeMarker)));

                string corruptZip = Path.Combine(root, "tampered-corrupt.zip");
                File.WriteAllText(corruptZip, "tampered-not-a-zip-archive");
                int rcCorrupt = HousekeepingInstallerRunner.Run(
                    ["--silent", "--no-shortcuts", "--no-launch", "--install-dir", Path.Combine(root, "corrupt"), "--data-dir", Path.Combine(root, "data_corrupt"), "--log-dir", Path.Combine(root, "logs_corrupt"), "--payload", corruptZip],
                    "0.2.0",
                    asm);
                Assert.NotEqual(0, rcCorrupt);
            }
            finally
            {
                Environment.SetEnvironmentVariable("APP_ISOLATED_TEST", prevIso);
                TestCleanupHelper.TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void AutomationFlagGate_FollowsBuildConfiguration_AndCliParserForcesMotherLoopback()
        {
            bool autoOk = HousekeepingInstallerIntegration.TryHandleAutomationArgs(
                ["--install-dir", "C:\\Temp\\x", "--automation-port", "51999", "--automation-token", "tok"],
                out string[] hostArgs,
                out string[] autoArgs,
                out string? errMsg);
#if DEBUG || HOUSEKEEPING_TEST_AUTOMATION
            Assert.True(autoOk, errMsg ?? string.Empty);
            Assert.Equal(["--install-dir", "C:\\Temp\\x"], hostArgs);
            Assert.Equal(["--automation-port", "51999", "--automation-token", "tok"], autoArgs);
#else
            Assert.False(autoOk);
            Assert.Contains("disabled in Release", errMsg ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            _ = hostArgs;
            _ = autoArgs;
#endif

            HousekeepingParsedCli cli = HousekeepingCliParser.Parse(
                ["--managed-by-suite", "--suite-id", "mother", "--bind", "0.0.0.0", "--port", "5178", "--automation-port", "51999"]);
            Assert.True(cli.ManagedByMother);
            Assert.Equal("mother", cli.SuiteId);
            Assert.Equal("127.0.0.1", cli.EffectiveBindHost);
            Assert.False(cli.EffectiveBindLanOverride);
            Assert.True(cli.IsAutomationRequested);
        }

        [Fact]
        public async Task MotherSubpathProxy_ServesScopedManifestSwHeaders_AndOfflineSvgQrOverLoopback()
        {
            string root = Path.Combine(Path.GetTempPath(), "hk_http_v020_" + Guid.NewGuid().ToString("N"));
            string dataDir = Path.Combine(root, "data");
            string logsDir = Path.Combine(root, "logs");
            string webRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Housekeeping.Server", "wwwroot"));
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(logsDir);
            Assert.True(File.Exists(Path.Combine(webRoot, "index.html")), $"Expected built SPA at {webRoot}");

            int port = GetFreeLoopbackPort();
            var options = new HousekeepingServerOptions
            {
                PortOverride = port,
                ManagedByMother = true,
                BindHostOverride = "127.0.0.1",
                SuiteId = "mother",
                CustomDataDirectory = dataDir,
                CustomConfigFilePath = Path.Combine(dataDir, "settings.json"),
                CustomLogsDirectory = logsDir,
                CustomWebRootPath = webRoot,
                IsolatedOverride = true,
                StartBackgroundTimers = false
            };

            using var app = HousekeepingServerHost.BuildWebApplication(Array.Empty<string>(), options);
            await app.StartAsync();
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

                using var manReq = new HttpRequestMessage(HttpMethod.Get, "/manifest.webmanifest");
                manReq.Headers.TryAddWithoutValidation("X-Forwarded-Prefix", "/app/housekeeping");
                using HttpResponseMessage manResp = await http.SendAsync(manReq);
                Assert.Equal(HttpStatusCode.OK, manResp.StatusCode);
                string manBody = await manResp.Content.ReadAsStringAsync();
                using var manDoc = JsonDocument.Parse(manBody);
                Assert.Equal("/app/housekeeping/", manDoc.RootElement.GetProperty("start_url").GetString());
                Assert.Equal("/app/housekeeping/", manDoc.RootElement.GetProperty("scope").GetString());

                using var swReq = new HttpRequestMessage(HttpMethod.Get, "/sw.js");
                swReq.Headers.TryAddWithoutValidation("X-Forwarded-Prefix", "/app/housekeeping");
                using HttpResponseMessage swResp = await http.SendAsync(swReq);
                Assert.Equal(HttpStatusCode.OK, swResp.StatusCode);
                Assert.True(swResp.Headers.TryGetValues("Service-Worker-Allowed", out var swVals));
                Assert.Equal("/app/housekeeping/", swVals!.First());

                using var qrReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/tailscale-qr?variant=mother&format=json");
                qrReq.Headers.TryAddWithoutValidation("X-Forwarded-Prefix", "/app/housekeeping");
                using HttpResponseMessage qrResp = await http.SendAsync(qrReq);
                Assert.Equal(HttpStatusCode.OK, qrResp.StatusCode);
                using var qrDoc = JsonDocument.Parse(await qrResp.Content.ReadAsStringAsync());
                Assert.Contains("<svg", qrDoc.RootElement.GetProperty("motherQrSvg").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("<svg", qrDoc.RootElement.GetProperty("directQrSvg").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                using HttpResponseMessage svgResp = await http.GetAsync("/api/v1/system/tailscale-qr.svg?variant=direct");
                Assert.Equal(HttpStatusCode.OK, svgResp.StatusCode);
                Assert.Equal("image/svg+xml", svgResp.Content.Headers.ContentType?.MediaType);
                Assert.Contains("<svg", await svgResp.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await app.StopAsync();
                TestCleanupHelper.TryDeleteDirectory(root);
            }
        }
    }
}
