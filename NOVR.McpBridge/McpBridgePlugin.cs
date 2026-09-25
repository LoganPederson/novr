using System;
using System.Security.Cryptography;
namespace NOVR.McpBridge;

// Development-only bridge that lets local tooling inspect the running game. It is not part of NOVR releases
// and stays off unless "Enabled" is turned on in its config.
[BepInEx.BepInPlugin("deltawing.novr.mcpbridge", "NOVR McpBridge", "0.1.0")]
public sealed class McpBridgePlugin : BepInEx.BaseUnityPlugin
{
    private const int DefaultPort = 3334;
    private McpHttpServer? _server;

    private void Awake()
    {
        var enabled = Config.Bind("General", "Enabled", false,
            "Start the local debugging server. Development use only: while it runs, programs on this PC can read game state.").Value;
        var port = Config.Bind("General", "Port", DefaultPort, "HTTP server port for MCP bridge").Value;
        var tokenEntry = Config.Bind("General", "Access Token", "",
            $"Secret every request must send in the {McpHttpServer.TokenHeader} header. Generated on first run; clear it to generate a new one.");

        if (!enabled)
        {
            Logger.LogInfo("MCP bridge is disabled (set Enabled = true in its config to use it for development).");
            return;
        }

        if (string.IsNullOrWhiteSpace(tokenEntry.Value))
            tokenEntry.Value = GenerateToken();

        ToolRegistry.DiscoverFromAssembly(typeof(McpBridgePlugin).Assembly);

        _ = MainThreadDispatcher.Instance;

        _server = new McpHttpServer(port, tokenEntry.Value);
        _server.Start();

        Logger.LogInfo($"MCP bridge started on http://localhost:{port}/ (requests need the {McpHttpServer.TokenHeader} header from its config)");
        Logger.LogInfo($"Registered {ToolRegistry.Tools.Count} tool(s): {string.Join(", ", ToolRegistry.Tools.Keys)}");
    }

    private static string GenerateToken()
    {
        var bytes = new byte[24];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(bytes);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private void OnDestroy()
    {
        _server?.Dispose();
    }
}
