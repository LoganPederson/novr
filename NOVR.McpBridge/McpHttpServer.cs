using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NOVR.McpBridge;

// Local-only HTTP bridge for development tools. Hardened because it runs inside the game:
// - binds loopback only;
// - rejects browser requests (any Origin header) and Host names other than localhost, so web pages can't reach it
//   through cross-site requests or DNS rebinding;
// - requires the access token from the plugin config on every request except /health;
// - caps request size, JSON nesting depth and queued main-thread work so a caller can't stall or crash the game.
public sealed class McpHttpServer : IDisposable
{
    public const string TokenHeader = "X-NOVR-Token";
    private const int MaxBodyChars = 64 * 1024;
    private const int MaxJsonDepth = 32;
    private const int MaxPendingInvocations = 16;

    private readonly HttpListener _listener = new();
    private readonly int _port;
    private readonly string _token;
    private bool _running;

    public McpHttpServer(int port, string token)
    {
        _port = port;
        _token = token;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        _ = ListenLoop();
    }

    public void Stop()
    {
        _running = false;
        try { _listener.Stop(); } catch { }
    }

    public void Dispose() => Stop();

    private async Task ListenLoop()
    {
        while (_running)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleRequest(ctx);
            }
            catch when (!_running) { break; }
            catch (Exception ex)
            {
                // Keep serving after a bad connection instead of silently stopping.
                UnityEngine.Debug.LogWarning($"[NOVR.McpBridge] Listener error: {ex.Message}");
                await Task.Delay(100);
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var rawUrl = ctx.Request.Url?.AbsolutePath?.Trim('/') ?? "";
            ctx.Response.ContentType = "application/json";

            var rejection = CheckRequest(ctx.Request, rawUrl);
            if (rejection != null)
            {
                await JsonResponse(ctx, rejection.Value.status, $"{{\"error\":\"{rejection.Value.message}\"}}");
                return;
            }

            switch (rawUrl)
            {
                case "health":
                    await JsonResponse(ctx, 200, "{\"status\":\"ok\"}");
                    break;

                case "tools":
                    await HandleGetTools(ctx);
                    break;

                case "invoke":
                    await HandleInvoke(ctx);
                    break;

                default:
                    await JsonResponse(ctx, 404, "{\"error\":\"Unknown endpoint\"}");
                    break;
            }
        }
        catch (Exception ex)
        {
            try
            {
                await JsonResponse(ctx, 500, $"{{\"error\":\"{ToolRegistry.EscapeJson(ex.Message)}\"}}");
            }
            catch { }
        }
    }

    private (int status, string message)? CheckRequest(HttpListenerRequest request, string path)
    {
        // Browsers always send Origin on cross-site requests; local dev tools don't.
        if (!string.IsNullOrEmpty(request.Headers["Origin"]))
            return (403, "Browser requests are not allowed");

        // A DNS-rebinding page reaches us under its own host name; only accept loopback names.
        var host = request.Headers["Host"] ?? "";
        if (host != $"localhost:{_port}" && host != $"127.0.0.1:{_port}")
            return (403, "Unexpected Host header");

        if (path == "health")
            return null;

        if (!TokenMatches(request.Headers[TokenHeader]))
            return (401, $"Missing or wrong {TokenHeader} header; the token is in BepInEx/config/deltawing.novr.mcpbridge.cfg");

        return null;
    }

    // Constant-time comparison so the token can't be recovered by timing responses.
    private bool TokenMatches(string? supplied)
    {
        if (supplied == null || supplied.Length != _token.Length) return false;
        var difference = 0;
        for (var i = 0; i < _token.Length; i++)
            difference |= supplied[i] ^ _token[i];
        return difference == 0;
    }

    private static int MaxNestingDepth(string json)
    {
        var depth = 0;
        var max = 0;
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (c == '"' && (i == 0 || json[i - 1] != '\\')) inString = !inString;
            if (inString) continue;
            if (c == '{' || c == '[') max = Math.Max(max, ++depth);
            else if (c == '}' || c == ']') depth--;
        }
        return max;
    }

    private async Task HandleGetTools(HttpListenerContext ctx)
    {
        var sb = new StringBuilder("[");
        var first = true;
        foreach (var tool in ToolRegistry.Tools)
        {
            if (!first) sb.Append(',');
            first = false;
            var name = tool.Key;
            var descriptor = tool.Value;
            sb.Append("{\"name\":\"");
            sb.Append(ToolRegistry.EscapeJson(name));
            sb.Append("\",\"description\":\"");
            sb.Append(ToolRegistry.EscapeJson(descriptor.Description));
            sb.Append("\",\"inputSchema\":");
            sb.Append(descriptor.InputSchemaJson);
            sb.Append('}');
        }
        sb.Append(']');

        await JsonResponse(ctx, 200, sb.ToString());
    }

    private async Task HandleInvoke(HttpListenerContext ctx)
    {
        if (ctx.Request.ContentLength64 > MaxBodyChars)
        {
            await JsonResponse(ctx, 413, "{\"error\":\"Request too large\"}");
            return;
        }

        // Read at most MaxBodyChars + 1 so a missing or false Content-Length can't make us buffer without limit.
        string body;
        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
        {
            var buffer = new char[MaxBodyChars + 1];
            var read = 0;
            int count;
            while (read < buffer.Length && (count = await reader.ReadAsync(buffer, read, buffer.Length - read)) > 0)
                read += count;
            if (read > MaxBodyChars)
            {
                await JsonResponse(ctx, 413, "{\"error\":\"Request too large\"}");
                return;
            }
            body = new string(buffer, 0, read);
        }

        // The parser below is recursive; bound nesting so a crafted body can't overflow the stack.
        if (MaxNestingDepth(body) > MaxJsonDepth)
        {
            await JsonResponse(ctx, 400, "{\"error\":\"JSON nested too deeply\"}");
            return;
        }

        if (MainThreadDispatcher.Instance.PendingCount >= MaxPendingInvocations)
        {
            await JsonResponse(ctx, 503, "{\"error\":\"Busy; retry shortly\"}");
            return;
        }

        var (toolName, args) = ParseInvokeRequest(body);

        if (!ToolRegistry.Tools.TryGetValue(toolName, out var descriptor))
        {
            await JsonResponse(ctx, 404, $"{{\"error\":\"Unknown tool: {ToolRegistry.EscapeJson(toolName)}\"}}");
            return;
        }

        var result = await MainThreadDispatcher.Instance.RunAsync(() =>
        {
            try { return ToolRegistry.Invoke(descriptor, args); }
            catch (Exception ex) { return $"<error>{ex.Message}"; }
        });

        var resultStr = ToolRegistry.ToJsonString(result);
        await JsonResponse(ctx, 200, $"{{\"result\":{resultStr}}}");
    }

    private static (string name, Dictionary<string, object?> args) ParseInvokeRequest(string json)
    {
        var trimmed = json.Trim();
        var toolName = "";
        var args = new Dictionary<string, object?>();

        if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
        {
            var content = trimmed.Substring(1, trimmed.Length - 2);
            foreach (var pair in SplitJsonPairs(content))
            {
                if (!pair.Contains(":")) continue;
                var colonIdx = pair.IndexOf(':');
                var key = UnescapeJsonString(pair.Substring(0, colonIdx).Trim());
                var rawVal = pair.Substring(colonIdx + 1).Trim();
                var val = ParseJsonValue(rawVal);

                if (key == "tool") toolName = val?.ToString() ?? "";
                else if (key == "args" && val is Dictionary<string, object?> dict) args = dict;
            }
        }

        return (toolName, args);
    }

    private static List<string> SplitJsonPairs(string content)
    {
        var pairs = new List<string>();
        var depth = 0;
        var start = 0;
        var inString = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '"' && (i == 0 || content[i - 1] != '\\')) inString = !inString;
            if (inString) continue;
            if (c == '{' || c == '[') depth++;
            if (c == '}' || c == ']') depth--;
            if (c == ',' && depth == 0)
            {
                pairs.Add(content.Substring(start, i - start));
                start = i + 1;
            }
        }
        if (start < content.Length) pairs.Add(content.Substring(start));

        return pairs;
    }

    private static object? ParseJsonValue(string raw)
    {
        raw = raw.Trim();
        if (raw == "null") return null;
        if (raw == "true") return true;
        if (raw == "false") return false;
        if (raw.StartsWith("\"") && raw.EndsWith("\""))
            return UnescapeJsonString(raw.Substring(1, raw.Length - 2));
        if (raw.StartsWith("{"))
        {
            var dict = new Dictionary<string, object?>();
            var inner = raw.Substring(1, raw.Length - 2);
            foreach (var pair in SplitJsonPairs(inner))
            {
                var colonIdx = pair.IndexOf(':');
                if (colonIdx < 0) continue;
                var key = UnescapeJsonString(pair.Substring(0, colonIdx).Trim());
                var val = ParseJsonValue(pair.Substring(colonIdx + 1).Trim());
                if (key != null) dict[key] = val;
            }
            return dict;
        }
        if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var num))
            return num;
        return raw;
    }

    private static string UnescapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length >= 2 && s.StartsWith("\"") && s.EndsWith("\""))
            s = s.Substring(1, s.Length - 2);

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                switch (s[i + 1])
                {
                    case '"': sb.Append('"'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    default: sb.Append(s[i]); break;
                }
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }

    private static Task JsonResponse(HttpListenerContext ctx, int status, string json)
    {
        ctx.Response.StatusCode = status;
        var buffer = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentLength64 = buffer.Length;
        return ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length)
            .ContinueWith(_ =>
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            });
    }
}
