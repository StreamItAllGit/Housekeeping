using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Housekeeping.Models;
using Housekeeping.Services;
using Microsoft.AspNetCore.Http;

namespace Housekeeping.Server.Security
{
    public sealed class AuthEvaluationResult
    {
        public bool IsAuthenticated { get; init; }
        public bool IsDirectLoopback { get; init; }
        public bool IsTailscaleServe { get; init; }
        public bool RequiresInitialPasswordSetup { get; init; }
        public string AuthMethod { get; init; } = "Anonymous";
        public string Identity { get; init; } = "";
    }

    /// <summary>
    /// Handles PBKDF2-SHA256 admin password hashing, 30-day HMAC-SHA256 signed session cookies,
    /// loopback auto-trust, Tailscale Serve identity header trust, and NodeApiToken verification.
    /// </summary>
    public sealed class TieredAuthService
    {
        public const string SessionCookieName = "hk_session";
        private const int Pbkdf2Iterations = 100_000;
        private const int SaltByteLength = 16;
        private const int HashByteLength = 32;
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

        private const int MaxFailedLoginsPerWindow = 5;
        private static readonly TimeSpan LoginFailureWindow = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan LoginLockoutDuration = TimeSpan.FromSeconds(30);

        private readonly ConfigService _configService;
        private readonly byte[] _cookieSigningKey;
        private readonly ConcurrentDictionary<string, (int Failures, DateTime WindowStartUtc, DateTime LockedUntilUtc)> _loginThrottle =
            new(StringComparer.OrdinalIgnoreCase);
        private IPAddress? _simulatedRemoteIpForTesting;

        public TieredAuthService(ConfigService configService)
        {
            _configService = configService;
            _cookieSigningKey = LoadOrCreateSigningKey(configService.DataDirectory, configService.ActiveLogsDir);
        }

        /// <summary>
        /// Used strictly in automated integration tests where TestServer supplies null RemoteIpAddress.
        /// </summary>
        public void SetSimulatedRemoteIpForTesting(IPAddress? ip)
        {
            _simulatedRemoteIpForTesting = ip;
        }

        private static byte[] LoadOrCreateSigningKey(string dataDir, string legacyLogsDir)
        {
            try
            {
                // Preserve existing sessions if legacy key already lives in logs\
                string legacyKeyPath = Path.Combine(legacyLogsDir, ".hk_cookie_secret.key");
                if (File.Exists(legacyKeyPath))
                {
                    string legacyHex = File.ReadAllText(legacyKeyPath, Encoding.UTF8).Trim();
                    if (legacyHex.Length >= 64)
                    {
                        return Convert.FromHexString(legacyHex[..64]);
                    }
                }

                Directory.CreateDirectory(dataDir);
                string keyPath = Path.Combine(dataDir, ".hk_cookie_secret.key");
                if (File.Exists(keyPath))
                {
                    string hex = File.ReadAllText(keyPath, Encoding.UTF8).Trim();
                    if (hex.Length >= 64)
                    {
                        return Convert.FromHexString(hex[..64]);
                    }
                }

                byte[] random = RandomNumberGenerator.GetBytes(32);
                File.WriteAllText(keyPath, Convert.ToHexString(random), Encoding.UTF8);
                return random;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TieredAuthService] Falling back to ephemeral cookie signing key: {ex.Message}");
                return RandomNumberGenerator.GetBytes(32);
            }
        }

        public bool IsPasswordConfigured()
        {
            var ws = _configService.Config.WebServer;
            return !string.IsNullOrWhiteSpace(ws.AdminPasswordHash) && !string.IsNullOrWhiteSpace(ws.AdminPasswordSalt);
        }

        public void SetAdminPassword(string plainPassword)
        {
            if (string.IsNullOrWhiteSpace(plainPassword) || plainPassword.Length < 4)
                throw new ArgumentException("Password must be at least 4 characters long.");

            byte[] salt = RandomNumberGenerator.GetBytes(SaltByteLength);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(plainPassword),
                salt,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                HashByteLength);

            string saltHex = Convert.ToHexString(salt);
            string hashHex = Convert.ToHexString(hash);
            _configService.MutateAndSave(cfg =>
            {
                cfg.WebServer.AdminPasswordSalt = saltHex;
                cfg.WebServer.AdminPasswordHash = hashHex;
            });
        }

        public bool VerifyAdminPassword(string candidatePassword)
        {
            if (string.IsNullOrEmpty(candidatePassword) || !IsPasswordConfigured())
                return false;

            var ws = _configService.Config.WebServer;
            try
            {
                byte[] salt = Convert.FromHexString(ws.AdminPasswordSalt);
                byte[] expectedHash = Convert.FromHexString(ws.AdminPasswordHash);
                byte[] candidateHash = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(candidatePassword),
                    salt,
                    Pbkdf2Iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(candidateHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }

        public string CreateSignedSessionToken(string identity = "admin")
        {
            long issuedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string safeIdentity = string.IsNullOrWhiteSpace(identity) ? "admin" : identity.Replace(":", "_");
            string payload = $"{issuedUnix}:{safeIdentity}";
            byte[] sig = HMACSHA256.HashData(_cookieSigningKey, Encoding.UTF8.GetBytes(payload));
            return $"{payload}:{Convert.ToHexString(sig).ToLowerInvariant()}";
        }

        public bool VerifySignedSessionToken(string? token, out string identity)
        {
            identity = "";
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var parts = token.Trim().Split(':');
            if (parts.Length != 3 || !long.TryParse(parts[0], out long issuedUnix))
                return false;

            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (issuedUnix > nowUnix + 300 || (nowUnix - issuedUnix) > (long)SessionLifetime.TotalSeconds)
                return false;

            string payload = $"{parts[0]}:{parts[1]}";
            byte[] expectedSig = HMACSHA256.HashData(_cookieSigningKey, Encoding.UTF8.GetBytes(payload));
            try
            {
                byte[] providedSig = Convert.FromHexString(parts[2]);
                if (!CryptographicOperations.FixedTimeEquals(providedSig, expectedSig))
                    return false;

                identity = parts[1];
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool IsEffectiveHttps(HttpContext context)
        {
            if (context.Request.IsHttps)
                return true;

            IPAddress? remoteIp = _simulatedRemoteIpForTesting ?? context.Connection.RemoteIpAddress;
            bool isRawLoopback = remoteIp == null || IPAddress.IsLoopback(remoteIp);
            if (isRawLoopback)
            {
                string forwardedProto = context.Request.Headers["X-Forwarded-Proto"].ToString().Trim();
                if (string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public bool IsLoginRateLimited(HttpContext context, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            string clientKey = ResolveThrottleKey(context);
            if (_loginThrottle.TryGetValue(clientKey, out var state))
            {
                var now = DateTime.UtcNow;
                if (state.LockedUntilUtc > now)
                {
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((state.LockedUntilUtc - now).TotalSeconds));
                    return true;
                }
            }
            return false;
        }

        public void RecordLoginAttempt(HttpContext context, bool succeeded)
        {
            string clientKey = ResolveThrottleKey(context);
            if (succeeded)
            {
                _loginThrottle.TryRemove(clientKey, out _);
                return;
            }

            if (_loginThrottle.Count > 256)
            {
                var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(10);
                foreach (var kvp in _loginThrottle)
                {
                    if (kvp.Value.WindowStartUtc < cutoff && kvp.Value.LockedUntilUtc < DateTime.UtcNow)
                    {
                        _loginThrottle.TryRemove(kvp.Key, out _);
                    }
                }
            }

            var now = DateTime.UtcNow;
            _loginThrottle.AddOrUpdate(
                clientKey,
                _ => (1, now, DateTime.MinValue),
                (_, prev) =>
                {
                    if (now - prev.WindowStartUtc > LoginFailureWindow)
                    {
                        return (1, now, DateTime.MinValue);
                    }

                    int nextFailures = prev.Failures + 1;
                    DateTime lockUntil = nextFailures >= MaxFailedLoginsPerWindow
                        ? now + LoginLockoutDuration
                        : DateTime.MinValue;
                    return (nextFailures, prev.WindowStartUtc, lockUntil);
                });
        }

        private string ResolveThrottleKey(HttpContext context)
        {
            IPAddress? remoteIp = _simulatedRemoteIpForTesting ?? context.Connection.RemoteIpAddress;
            bool isRawLoopback = remoteIp == null || IPAddress.IsLoopback(remoteIp);
            if (isRawLoopback && context.Request.Headers.TryGetValue("X-Forwarded-For", out var xff))
            {
                string firstHop = xff.ToString().Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(firstHop))
                    return $"xff:{firstHop}";
            }
            return remoteIp?.ToString() ?? "loopback";
        }

        public void IssueSessionCookie(HttpResponse response, bool isHttps, string identity = "admin")
        {
            string token = CreateSignedSessionToken(identity);
            response.Cookies.Append(SessionCookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = isHttps,
                Path = "/",
                MaxAge = SessionLifetime
            });
        }

        public void ClearSessionCookie(HttpResponse response)
        {
            response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/" });
        }

        public AuthEvaluationResult EvaluateRequest(HttpContext context)
        {
            var cfg = _configService.Config;
            var ws = cfg.WebServer;
            ws.EnsureNodeApiTokenInitialized();

            IPAddress? remoteIp = _simulatedRemoteIpForTesting ?? context.Connection.RemoteIpAddress;
            bool isRawLoopback = remoteIp == null || IPAddress.IsLoopback(remoteIp);
            bool hasForwardedFor = context.Request.Headers.ContainsKey("X-Forwarded-For");
            string tailscaleLogin = context.Request.Headers["Tailscale-User-Login"].ToString().Trim();

            // 1. Machine-to-Machine Node API Token (X-Housekeeping-Token or Authorization: Bearer)
            string? providedApiToken = context.Request.Headers["X-Housekeeping-Token"].ToString().Trim();
            if (string.IsNullOrEmpty(providedApiToken))
            {
                string authHeader = context.Request.Headers.Authorization.ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    providedApiToken = authHeader["Bearer ".Length..].Trim();
                }
            }

            if (!string.IsNullOrEmpty(providedApiToken) && ConstantTimeEquals(providedApiToken, ws.NodeApiToken))
            {
                return new AuthEvaluationResult
                {
                    IsAuthenticated = true,
                    IsDirectLoopback = isRawLoopback && !hasForwardedFor,
                    AuthMethod = "NodeApiToken",
                    Identity = "fleet-node"
                };
            }

            // 2. Tailscale Serve Loopback Identity Header
            if (isRawLoopback && ws.TrustTailscaleServeHeaders && !string.IsNullOrEmpty(tailscaleLogin))
            {
                return new AuthEvaluationResult
                {
                    IsAuthenticated = true,
                    IsDirectLoopback = false,
                    IsTailscaleServe = true,
                    AuthMethod = "TailscaleServe",
                    Identity = tailscaleLogin
                };
            }

            // 3. Direct Localhost Loopback Auto-Trust (Browser or WebView2 on the host PC)
            if (isRawLoopback && !hasForwardedFor)
            {
                return new AuthEvaluationResult
                {
                    IsAuthenticated = true,
                    IsDirectLoopback = true,
                    RequiresInitialPasswordSetup = !IsPasswordConfigured(),
                    AuthMethod = "LocalLoopback",
                    Identity = $"{Environment.MachineName}\\{Environment.UserName}"
                };
            }

            // 4. Signed Session Cookie (LAN / Tailnet / Remote Browser or Query Token for SignalR)
            if (context.Request.Cookies.TryGetValue(SessionCookieName, out string? cookieVal) &&
                VerifySignedSessionToken(cookieVal, out string cookieUser))
            {
                return new AuthEvaluationResult
                {
                    IsAuthenticated = true,
                    IsDirectLoopback = false,
                    AuthMethod = "SessionCookie",
                    Identity = cookieUser
                };
            }

            string accessTokenQuery = context.Request.Query["access_token"].ToString();
            if (!string.IsNullOrEmpty(accessTokenQuery) &&
                (ConstantTimeEquals(accessTokenQuery, ws.NodeApiToken) || VerifySignedSessionToken(accessTokenQuery, out _)))
            {
                return new AuthEvaluationResult
                {
                    IsAuthenticated = true,
                    IsDirectLoopback = false,
                    AuthMethod = "SignalRQueryToken",
                    Identity = "remote-hub"
                };
            }

            return new AuthEvaluationResult
            {
                IsAuthenticated = false,
                IsDirectLoopback = false,
                RequiresInitialPasswordSetup = !IsPasswordConfigured(),
                AuthMethod = "Unauthenticated"
            };
        }

        private static bool ConstantTimeEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            byte[] ba = Encoding.UTF8.GetBytes(a);
            byte[] bb = Encoding.UTF8.GetBytes(b);
            return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
        }
    }
}
