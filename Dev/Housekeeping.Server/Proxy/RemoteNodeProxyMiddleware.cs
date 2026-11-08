using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Housekeeping.Services;
using Microsoft.AspNetCore.Http;

namespace Housekeeping.Server.Proxy
{
    /// <summary>
    /// Transparent server-side multi-node HTTP proxy enabling the Unified Top-Bar Fleet Node Switcher.
    /// Any /api/v1/* request carrying X-Target-Node-Id != "local" is forwarded to the configured
    /// LAN / Tailnet remote Housekeeping 3.0 node authenticated with that node's ApiToken.
    /// </summary>
    public sealed class RemoteNodeProxyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ConfigService _configService;
        private readonly HttpClient _proxyClient;

        public RemoteNodeProxyMiddleware(
            RequestDelegate next,
            ConfigService configService,
            HttpClient? customProxyClient = null)
        {
            _next = next;
            _configService = configService;
            _proxyClient = customProxyClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string path = context.Request.Path.Value ?? "/";
            if (!path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Auth status & local fleet node management always execute against the local coordinator
            if (path.StartsWith("/api/v1/auth/", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Headers.ContainsKey("X-Bypass-Node-Proxy"))
            {
                await _next(context);
                return;
            }

            string targetNodeId = context.Request.Headers["X-Target-Node-Id"].ToString().Trim();
            if (string.IsNullOrEmpty(targetNodeId))
            {
                targetNodeId = context.Request.Query["targetNodeId"].ToString().Trim();
            }

            if (string.IsNullOrEmpty(targetNodeId) || string.Equals(targetNodeId, "local", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var node = _configService.Config.RemoteNodes
                .FirstOrDefault(n => string.Equals(n.Id, targetNodeId, StringComparison.OrdinalIgnoreCase));

            if (node == null || string.IsNullOrWhiteSpace(node.HostOrIp))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "remote_node_not_found",
                    message = $"Target remote node '{targetNodeId}' is not configured."
                });
                return;
            }

            int port = node.HttpApiPort > 0 ? node.HttpApiPort : 5178;
            string hostPart = node.HostOrIp.Trim();
            string baseUri = hostPart.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             hostPart.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? hostPart.TrimEnd('/')
                : $"http://{hostPart}:{port}";

            string targetUrl = $"{baseUri}{path}{context.Request.QueryString}";

            try
            {
                using var forwardReq = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);
                if (!string.IsNullOrWhiteSpace(node.ApiToken))
                {
                    forwardReq.Headers.TryAddWithoutValidation("X-Housekeeping-Token", node.ApiToken.Trim());
                }
                // Prevent recursive proxy loops if remote node also has node configs
                forwardReq.Headers.TryAddWithoutValidation("X-Target-Node-Id", "local");

                if (context.Request.ContentLength > 0 ||
                    HttpMethods.IsPost(context.Request.Method) ||
                    HttpMethods.IsPut(context.Request.Method) ||
                    HttpMethods.IsPatch(context.Request.Method))
                {
                    var ms = new MemoryStream();
                    await context.Request.Body.CopyToAsync(ms, context.RequestAborted);
                    ms.Position = 0;
                    var streamContent = new StreamContent(ms);
                    if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
                    {
                        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
                    }
                    forwardReq.Content = streamContent;
                }

                using var remoteResp = await _proxyClient.SendAsync(
                    forwardReq,
                    HttpCompletionOption.ResponseHeadersRead,
                    context.RequestAborted);

                context.Response.StatusCode = (int)remoteResp.StatusCode;
                if (remoteResp.Content.Headers.ContentType != null)
                {
                    context.Response.ContentType = remoteResp.Content.Headers.ContentType.ToString();
                }

                await remoteResp.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "remote_node_unreachable",
                    nodeId = node.Id,
                    nodeName = node.Name,
                    targetUrl,
                    message = $"Could not reach Housekeeping API on '{node.Name}' ({baseUri}): {ex.Message}"
                });
            }
        }
    }
}
