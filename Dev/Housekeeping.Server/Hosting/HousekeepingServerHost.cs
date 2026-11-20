// @module: Housekeeping.Server.Hosting.HousekeepingServerHost
// @purpose: Configures Kestrel (with Mother loopback lock), X-Forwarded-Prefix subpath routing, dynamic PWA manifest/SW scope headers, SignalR, and SPA fallback.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Net, System.Net.Http, System.Text, System.Text.Json, System.Text.Json.Serialization, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Hosting, Microsoft.AspNetCore.Http, Microsoft.AspNetCore.StaticFiles, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.FileProviders, Microsoft.Extensions.Hosting, Microsoft.Extensions.Logging
// @exports: HousekeepingServerOptions, HousekeepingServerHost

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Housekeeping.Server.Endpoints;
using Housekeeping.Server.Hubs;
using Housekeeping.Server.Proxy;
using Housekeeping.Server.Security;
using Housekeeping.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
        public string? BindHostOverride { get; set; }
        public bool ManagedByMother { get; set; }
        public string? SuiteId { get; set; }
        public string? CustomDataDirectory { get; set; }
        public string? CustomConfigFilePath { get; set; }
        public string? CustomLogsDirectory { get; set; }
        public string? CustomWebRootPath { get; set; }
        public bool IsolatedOverride { get; set; }
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
            bool managedByMother = ResolveManagedByMother(options, args);
            string? bindHost = managedByMother ? "127.0.0.1" : ResolveBindHost(options, args);
            options.ManagedByMother = managedByMother;
            options.BindHostOverride = bindHost;

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
                isolatedOverride: options.IsolatedOverride,
                forceDryRun: options.DryRun);

            if (options.NativeDialogBridge != null)
            {
                runtime.NativeDialogs = options.NativeDialogBridge;
            }

            if (!options.UseTestServerUrls)
            {
                int port = options.PortOverride ?? runtime.ConfigService.Config.WebServer.Port;
                if (port <= 0 || port > 65535) port = 5178;
                ConfigureKestrelBinding(builder, port, managedByMother, bindHost, options.BindLanOverride, runtime);
            }

            builder.Services.ConfigureHttpJsonOptions(json =>
            {
                json.SerializerOptions.PropertyNamingPolicy = null; // Preserve PascalCase C# contract + case-insensitive input
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

            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(runtime);
            builder.Services.AddSingleton(runtime.ConfigService);
            builder.Services.AddSingleton<TieredAuthService>();
            builder.Services.AddSingleton<TailscalePwaService>();
            builder.Services.AddHostedService<RealtimeBroadcastHostedService>();

            var app = builder.Build();

            app.UsePathBase("/app/housekeeping");
            app.Use(async (ctx, next) =>
            {
                string? forwardedPrefix = NormalizeForwardedPrefix(ctx.Request.Headers["X-Forwarded-Prefix"].ToString());
                if (!string.IsNullOrEmpty(forwardedPrefix))
                {
                    if (ctx.Request.Path.StartsWithSegments(forwardedPrefix, StringComparison.OrdinalIgnoreCase, out PathString remaining))
                    {
                        ctx.Request.PathBase = forwardedPrefix;
                        ctx.Request.Path = remaining.HasValue ? remaining : "/";
                    }
                    else if (!ctx.Request.PathBase.HasValue)
                    {
                        ctx.Request.PathBase = forwardedPrefix;
                    }
                }
                await next();
            });

            app.UseMiddleware<TieredAuthMiddleware>();
            if (options.CustomRemoteProxyHttpClient != null)
                app.UseMiddleware<RemoteNodeProxyMiddleware>(options.CustomRemoteProxyHttpClient);
            else
                app.UseMiddleware<RemoteNodeProxyMiddleware>();

            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Path.Equals("/manifest.webmanifest", StringComparison.OrdinalIgnoreCase) &&
                    (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)))
                {
                    string scopedJson = BuildScopedManifestJson(webRoot, ResolveEffectiveScopeRoot(ctx));
                    ctx.Response.Headers.CacheControl = "no-cache, must-revalidate";
                    ctx.Response.ContentType = "application/manifest+json; charset=utf-8";
                    if (!HttpMethods.IsHead(ctx.Request.Method))
                    {
                        await ctx.Response.WriteAsync(scopedJson, Encoding.UTF8, ctx.RequestAborted);
                    }
                    return;
                }
                await next();
            });

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
                            ctx.Context.Response.Headers["Service-Worker-Allowed"] = ResolveEffectiveScopeRoot(ctx.Context);
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

        private static void ConfigureKestrelBinding(
            WebApplicationBuilder builder,
            int port,
            bool managedByMother,
            string? bindHost,
            bool? bindLanOverride,
            HousekeepingRuntimeContainer runtime)
        {
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                if (managedByMother)
                {
                    kestrel.Listen(IPAddress.Loopback, port);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(bindHost))
                {
                    string h = bindHost.Trim();
                    if (h is "0.0.0.0" or "*" or "any" or "::")
                        kestrel.ListenAnyIP(port);
                    else if (h.Equals("localhost", StringComparison.OrdinalIgnoreCase) || h == "127.0.0.1")
                        kestrel.Listen(IPAddress.Loopback, port);
                    else if (IPAddress.TryParse(h, out var parsedIp))
                        kestrel.Listen(parsedIp, port);
                    else
                        kestrel.Listen(IPAddress.Loopback, port);
                    return;
                }

                bool bindAll = bindLanOverride ?? runtime.ConfigService.Config.WebServer.BindLanAndTailnet;
                if (bindAll)
                    kestrel.ListenAnyIP(port);
                else
                    kestrel.Listen(IPAddress.Loopback, port);
            });
        }

        private static bool ResolveManagedByMother(HousekeepingServerOptions options, string[] args)
        {
            if (options.ManagedByMother) return true;
            if (args.Any(a => a.Equals("--managed-by-mother", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("--managed-by-suite", StringComparison.OrdinalIgnoreCase)))
                return true;

            foreach (string key in new[] { "HOUSEKEEPING_MANAGED_BY_MOTHER", "MOTHER_MANAGED", "MANAGED_BY_SUITE", "MOTHER_SUPERVISED" })
            {
                string? val = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(val) &&
                    (val.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                     val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                     val.Equals("yes", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        private static string? ResolveBindHost(HousekeepingServerOptions options, string[] args)
        {
            if (!string.IsNullOrWhiteSpace(options.BindHostOverride)) return options.BindHostOverride.Trim();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Equals("--bind", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return args[i + 1].Trim();
                if (a.StartsWith("--bind=", StringComparison.OrdinalIgnoreCase))
                    return a["--bind=".Length..].Trim();
            }
            return null;
        }

        private static string? NormalizeForwardedPrefix(string? rawHeader)
        {
            if (string.IsNullOrWhiteSpace(rawHeader)) return null;
            string first = rawHeader.Split(',')[0].Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(first) || !first.StartsWith('/') || first.Contains("..", StringComparison.Ordinal) ||
                first.Contains('\\') || first.Any(char.IsWhiteSpace) || first.Any(char.IsControl))
                return null;
            return first;
        }

        private static string ResolveEffectiveScopeRoot(HttpContext ctx)
        {
            string? pathBase = ctx.Request.PathBase.HasValue ? ctx.Request.PathBase.Value?.TrimEnd('/') : null;
            if (string.IsNullOrEmpty(pathBase))
                pathBase = NormalizeForwardedPrefix(ctx.Request.Headers["X-Forwarded-Prefix"].ToString());
            return string.IsNullOrEmpty(pathBase) ? "/" : $"{pathBase}/";
        }

        private static string BuildScopedManifestJson(string webRoot, string scopeRoot)
        {
            string manifestFile = Path.Combine(webRoot, "manifest.webmanifest");
            JsonObject obj;
            if (File.Exists(manifestFile))
            {
                try
                {
                    obj = JsonNode.Parse(File.ReadAllText(manifestFile, Encoding.UTF8)) as JsonObject ?? new JsonObject();
                }
                catch
                {
                    obj = new JsonObject();
                }
            }
            else
            {
                obj = new JsonObject();
            }

            obj["id"] = scopeRoot;
            obj["name"] ??= "Housekeeping";
            obj["short_name"] ??= "Housekeeping";
            obj["description"] ??= "Always-on Media, Archive, Cloud, Manifest & ROM Automation Engine";
            obj["start_url"] = scopeRoot;
            obj["scope"] = scopeRoot;
            obj["display"] ??= "standalone";
            obj["background_color"] ??= "#090e17";
            obj["theme_color"] ??= "#090e17";
            obj["icons"] = new JsonArray(
                new JsonObject { ["src"] = "./icon-192.png", ["sizes"] = "192x192", ["type"] = "image/png", ["purpose"] = "any" },
                new JsonObject { ["src"] = "./icon-512.png", ["sizes"] = "512x512", ["type"] = "image/png", ["purpose"] = "any" },
                new JsonObject { ["src"] = "./icon-maskable-192.png", ["sizes"] = "192x192", ["type"] = "image/png", ["purpose"] = "maskable" },
                new JsonObject { ["src"] = "./icon-maskable-512.png", ["sizes"] = "512x512", ["type"] = "image/png", ["purpose"] = "maskable" }
            );
            return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        public static string ResolveWebRootPath(string? customOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(customOverride) && Directory.Exists(customOverride))
                return Path.GetFullPath(customOverride);

            string baseDirWwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            if (Directory.Exists(baseDirWwwroot))
                return baseDirWwwroot;

            string candidate = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Housekeeping.Server", "wwwroot"));
            if (Directory.Exists(candidate))
                return candidate;

            Directory.CreateDirectory(baseDirWwwroot);
            return baseDirWwwroot;
        }
    }
}
