using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using Housekeeping.Server.Endpoints;
using Housekeeping.Server.Hubs;
using Housekeeping.Server.Proxy;
using Housekeeping.Server.Security;
using Housekeeping.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Housekeeping.Server.Hosting
{
    public sealed class HousekeepingServerOptions
    {
        public int? PortOverride { get; set; }
        public bool? BindLanOverride { get; set; }
        public string? CustomDataDirectory { get; set; }
        public string? CustomConfigFilePath { get; set; }
        public string? CustomLogsDirectory { get; set; }
        public string? CustomWebRootPath { get; set; }
        public bool TestMode { get; set; }
        public bool DryRun { get; set; }
        public bool StartBackgroundTimers { get; set; } = true;
        public HttpClient? CustomGreenroomHttpClient { get; set; }
        public HttpClient? CustomRemoteProxyHttpClient { get; set; }
        public INativeDialogBridge? NativeDialogBridge { get; set; }
        public bool UseTestServerUrls { get; set; } = false;
    }

    public static class HousekeepingServerHost
    {
        public static WebApplication BuildWebApplication(
            string[] args,
            HousekeepingServerOptions? options = null,
            HousekeepingRuntimeContainer? prebuiltRuntime = null)
        {
            options ??= new HousekeepingServerOptions();

            string webRoot = ResolveWebRootPath(options.CustomWebRootPath);
            var builderOptions = new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ContentRootPath = AppDomain.CurrentDomain.BaseDirectory,
                WebRootPath = webRoot
            };

            var builder = WebApplication.CreateBuilder(builderOptions);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            var runtime = prebuiltRuntime ?? new HousekeepingRuntimeContainer(
                customDataDirectory: options.CustomDataDirectory,
                customGreenroomHttpClient: options.CustomGreenroomHttpClient,
                nativeDialogs: options.NativeDialogBridge,
                startBackgroundTimers: options.StartBackgroundTimers,
                customConfigFilePath: options.CustomConfigFilePath,
                customLogsDirectory: options.CustomLogsDirectory,
                isTestMode: options.TestMode,
                forceDryRun: options.DryRun);

            if (options.NativeDialogBridge != null)
            {
                runtime.NativeDialogs = options.NativeDialogBridge;
            }

            if (!options.UseTestServerUrls)
            {
                int port = options.PortOverride ?? runtime.ConfigService.Config.WebServer.Port;
                if (port <= 0 || port > 65535) port = 5178;
                bool bindAll = options.BindLanOverride ?? runtime.ConfigService.Config.WebServer.BindLanAndTailnet;

                builder.WebHost.ConfigureKestrel(kestrel =>
                {
                    if (bindAll)
                        kestrel.ListenAnyIP(port);
                    else
                        kestrel.Listen(IPAddress.Loopback, port);
                });
            }

            builder.Services.ConfigureHttpJsonOptions(json =>
            {
                json.SerializerOptions.PropertyNamingPolicy = null; // Preserve clean PascalCase C# contract + case-insensitive input
                json.SerializerOptions.PropertyNameCaseInsensitive = true;
                json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));
            });

            builder.Services.AddSignalR()
                .AddJsonProtocol(json =>
                {
                    json.PayloadSerializerOptions.PropertyNamingPolicy = null;
                    json.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                    json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));
                });

            builder.Services.AddSingleton(runtime);
            builder.Services.AddSingleton(runtime.ConfigService);
            builder.Services.AddSingleton<TieredAuthService>();
            builder.Services.AddSingleton<TailscalePwaService>();
            builder.Services.AddHostedService<RealtimeBroadcastHostedService>();

            var app = builder.Build();

            app.UseMiddleware<TieredAuthMiddleware>();
            if (options.CustomRemoteProxyHttpClient != null)
            {
                app.UseMiddleware<RemoteNodeProxyMiddleware>(options.CustomRemoteProxyHttpClient);
            }
            else
            {
                app.UseMiddleware<RemoteNodeProxyMiddleware>();
            }

            if (Directory.Exists(webRoot))
            {
                var provider = new PhysicalFileProvider(webRoot);
                var contentTypeProvider = new FileExtensionContentTypeProvider();
                contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";

                app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = provider,
                    ContentTypeProvider = contentTypeProvider,
                    OnPrepareResponse = ctx =>
                    {
                        string reqPath = ctx.Context.Request.Path.Value ?? "";
                        if (reqPath.Equals("/sw.js", StringComparison.OrdinalIgnoreCase))
                        {
                            ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
                            ctx.Context.Response.Headers["Service-Worker-Allowed"] = "/";
                        }
                        else if (reqPath.Equals("/manifest.webmanifest", StringComparison.OrdinalIgnoreCase) ||
                                 reqPath.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
                        {
                            ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
                        }
                    }
                });
            }

            app.MapHousekeepingApi();
            app.MapHub<HousekeepingHub>("/hubs/housekeeping");

            string indexHtml = Path.Combine(webRoot, "index.html");
            if (File.Exists(indexHtml))
            {
                app.MapFallback(async ctx =>
                {
                    string p = ctx.Request.Path.Value ?? "/";
                    if (p.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                        p.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Response.StatusCode = 404;
                        return;
                    }
                    ctx.Response.Headers.CacheControl = "no-cache, must-revalidate";
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    await ctx.Response.SendFileAsync(indexHtml);
                });
            }

            return app;
        }

        public static string ResolveWebRootPath(string? customOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(customOverride) && Directory.Exists(customOverride))
                return Path.GetFullPath(customOverride);

            string baseDirWwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            if (Directory.Exists(baseDirWwwroot))
                return baseDirWwwroot;

            // Developer workspace fallback when running directly from bin/Release/net8.0-windows
            string candidate = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Housekeeping.Server", "wwwroot"));
            if (Directory.Exists(candidate))
                return candidate;

            Directory.CreateDirectory(baseDirWwwroot);
            return baseDirWwwroot;
        }
    }
}
