using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VmMcp;

/// <summary>
/// Minimal Model Context Protocol server over stdio (newline-delimited JSON-RPC 2.0).
/// Hand-rolled on purpose: the repository must build on a machine where NuGet restore
/// is not guaranteed, and the protocol surface we need is four methods.
/// </summary>
internal static class Program
{
    private const string ServerName = "vm-tools";
    private const string ServerVersion = "1.0.0";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly HashSet<string> KnownProtocols = new(StringComparer.Ordinal)
    {
        "2024-11-05", "2025-03-26", "2025-06-18",
    };

    [STAThread]
    private static int Main(string[] args)
    {
        Native.EnableDpiAwareness();

        string? root = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--root", StringComparison.OrdinalIgnoreCase)) root = args[i + 1];
        }
        Paths.Init(root);

        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase))) return SelfTest();
        if (args.Any(a => a.Equals("--list-tools", StringComparison.OrdinalIgnoreCase))) return ListTools();
        if (args.Any(a => a.Equals("--selftest-window", StringComparison.OrdinalIgnoreCase))) return SelfTestWindow.Run();

        try
        {
            RunServer();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[vm-mcp] fatal: {ex}");
            return 1;
        }
    }

    private static int SelfTest()
    {
        Console.WriteLine($"repo          = {Paths.RepoRoot}");
        Console.WriteLine($"tools         = {Tools.All.Count} ({string.Join(", ", Tools.All.Select(t => t.Name))})");
        Console.WriteLine($"visionmaster  = {(File.Exists(Paths.VisionMasterExe) ? Paths.VisionMasterExe : "MISSING")}");
        Console.WriteLine($"visionmaster  = running: {Proc.VisionMasterProcesses().Count} instance(s)");
        Console.WriteLine($"logs          = {Paths.LogsDir}");
        Console.WriteLine($"artifacts     = {Paths.Artifacts}");

        try
        {
            var status = Tools.All.First(t => t.Name == "vm_status").Handler(new JsonObject());
            Console.WriteLine("--- vm_status ---");
            Console.WriteLine(status);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"vm_status failed: {ex.Message}");
            return 1;
        }
        return 0;
    }

    private static int ListTools()
    {
        foreach (var t in Tools.All)
        {
            Console.WriteLine($"{t.Name,-16} {t.Description.Split('.')[0]}.");
        }
        return 0;
    }

    private static void RunServer()
    {
        using var stdin = new StreamReader(Console.OpenStandardInput(), Utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 65536);
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), Utf8NoBom, bufferSize: 65536)
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        Console.Error.WriteLine($"[vm-mcp] ready: repo={Paths.RepoRoot} tools={Tools.All.Count}");

        string? line;
        while ((line = stdin.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonNode? msg;
            try
            {
                msg = JsonNode.Parse(line);
            }
            catch (JsonException ex)
            {
                Write(stdout, Error(null, -32700, "parse error: " + ex.Message));
                continue;
            }

            if (msg is not JsonObject obj) continue;

            var id = obj["id"];
            var method = obj["method"]?.GetValue<string>();
            if (method is null) continue; // a response, not a request — ignore

            // Notifications carry no id and must never get a reply.
            if (id is null)
            {
                if (method == "notifications/initialized" || method == "notifications/cancelled") continue;
                if (method == "exit") return;
                continue;
            }

            switch (method)
            {
                case "initialize":
                    Write(stdout, Ok(id, InitializeResult(obj)));
                    break;

                case "ping":
                    Write(stdout, Ok(id, new JsonObject()));
                    break;

                case "tools/list":
                    Write(stdout, Ok(id, ToolsListResult()));
                    break;

                case "tools/call":
                    Write(stdout, CallTool(id, obj));
                    break;

                case "resources/list":
                    Write(stdout, Ok(id, new JsonObject { ["resources"] = new JsonArray() }));
                    break;

                case "prompts/list":
                    Write(stdout, Ok(id, new JsonObject { ["prompts"] = new JsonArray() }));
                    break;

                case "shutdown":
                    Write(stdout, Ok(id, new JsonObject()));
                    return;

                default:
                    Write(stdout, Error(id, -32601, $"method not found: {method}"));
                    break;
            }
        }
    }

    private static JsonObject InitializeResult(JsonObject request)
    {
        string requested = "";
        try { requested = request["params"]?["protocolVersion"]?.GetValue<string>() ?? ""; } catch { }
        string version = KnownProtocols.Contains(requested) ? requested : "2024-11-05";

        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
            },
        };
    }

    private static JsonObject ToolsListResult()
    {
        var arr = new JsonArray();
        foreach (var t in Tools.All)
        {
            arr.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema.DeepClone(),
            });
        }
        return new JsonObject { ["tools"] = arr };
    }

    private static JsonObject CallTool(JsonNode? id, JsonObject request)
    {
        var sw = Stopwatch.StartNew();
        string name = "";
        try
        {
            name = request["params"]?["name"]?.GetValue<string>() ?? "";
            var args = request["params"]?["arguments"] as JsonObject ?? new JsonObject();

            var tool = Tools.All.FirstOrDefault(t => t.Name == name);
            if (tool is null) return Error(id, -32602, $"unknown tool: {name}");

            var text = tool.Handler(args);
            sw.Stop();
            Console.Error.WriteLine($"[vm-mcp] {name} ok {sw.ElapsedMilliseconds}ms");
            return Ok(id, new JsonObject
            {
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
                ["isError"] = false,
            });
        }
        catch (AssertionFailed ex)
        {
            // The assertion ran and failed. The report is the evidence, so it goes out
            // verbatim with isError=true — never prefixed in a way that hides the verdict.
            sw.Stop();
            Console.Error.WriteLine($"[vm-mcp] {name} ASSERT-FAIL {sw.ElapsedMilliseconds}ms");
            return Ok(id, new JsonObject
            {
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = ex.Message } },
                ["isError"] = true,
            });
        }
        catch (ToolException ex)
        {
            sw.Stop();
            Console.Error.WriteLine($"[vm-mcp] {name} TOOL-ERROR {sw.ElapsedMilliseconds}ms: {ex.Message}");
            return Ok(id, new JsonObject
            {
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "ERROR: " + ex.Message } },
                ["isError"] = true,
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.Error.WriteLine($"[vm-mcp] {name} CRASH {sw.ElapsedMilliseconds}ms: {ex}");
            return Ok(id, new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = $"ERROR: {ex.GetType().Name}: {ex.Message}" },
                },
                ["isError"] = true,
            });
        }
    }

    private static JsonObject Ok(JsonNode? id, JsonObject result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result,
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private static void Write(StreamWriter stdout, JsonObject payload)
    {
        stdout.WriteLine(payload.ToJsonString(Json.Options));
    }
}
