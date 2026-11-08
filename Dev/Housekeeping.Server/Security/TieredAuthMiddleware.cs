using System;
using System.Threading.Tasks;
using Housekeeping.Services;
using Microsoft.AspNetCore.Http;

namespace Housekeeping.Server.Security
{
    public sealed class TieredAuthMiddleware
    {
        public const string AuthResultItemKey = "Housekeeping.AuthEvaluation";

        private readonly RequestDelegate _next;
        private readonly TieredAuthService _authService;
        private readonly ConfigService _configService;

        public TieredAuthMiddleware(RequestDelegate next, TieredAuthService authService, ConfigService configService)
        {
            _next = next;
            _authService = authService;
            _configService = configService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string path = context.Request.Path.Value ?? "/";
            var eval = _authService.EvaluateRequest(context);
            context.Items[AuthResultItemKey] = eval;

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.CacheControl = "no-store";
            }

            // Allow static SPA shell & public auth handshake endpoints so the login/setup gate can render
            if (IsPublicEndpoint(path))
            {
                await _next(context);
                return;
            }

            if (!eval.IsAuthenticated)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "authentication_required",
                    requiresSetup = eval.RequiresInitialPasswordSetup,
                    message = eval.RequiresInitialPasswordSetup
                        ? "First-time remote access requires configuring an Admin Password."
                        : "Please sign in with your Housekeeping Admin Password."
                });
                return;
            }

            // Enforce RestrictRawShellToLocalhost when accessed remotely
            if (!eval.IsDirectLoopback && _configService.Config.WebServer.RestrictRawShellToLocalhost)
            {
                if (path.Equals("/api/v1/terminal/exec", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "raw_shell_restricted_to_localhost",
                        message = "Interactive raw shell execution is restricted to localhost while 'Restrict Raw Shell & Script Editing to Localhost' is enabled."
                    });
                    return;
                }
            }

            await _next(context);
        }

        private static bool IsPublicEndpoint(string path)
        {
            if (path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/v1/auth/status", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/v1/auth/setup-password", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Static SPA shell, CSS/JS bundles, PWA manifest, icons
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
