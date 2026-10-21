using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using VisionMaster.Lifetime;

namespace Probe.Host
{
    /// <summary>
    /// M0 probe: does "optional module" work under real .NET 9 layout?
    /// Output is ASCII-only on purpose (console codepage safety).
    ///
    /// Env vars:
    ///   PROBE_CONFIG  - path to AppConfig.json (default: &lt;baseDir&gt;\AppConfig.json)
    ///   PROBE_WEB_DIR - module directory    (default: &lt;baseDir&gt;\Web)
    /// </summary>
    internal static class Program
    {
        private static readonly Dictionary<string, string> Verdicts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = "SKIP", ["B"] = "SKIP", ["C"] = "SKIP",
            ["D"] = "SKIP", ["E"] = "SKIP", ["F"] = "SKIP"
        };

        private static void Check(string id, bool ok, string detail)
        {
            Verdicts[id] = ok ? "PASS" : "FAIL";
            Console.WriteLine("[" + (ok ? "PASS" : "FAIL") + "] " + id + " - " + detail);
        }

        private static int Main(string[] args)
        {
            Console.WriteLine("=== Probe.Host (M0: optional IAppModule loading) ===");
            Console.WriteLine("BaseDirectory           : " + AppContext.BaseDirectory);
            Console.WriteLine("Runtime                 : " + Environment.Version);
            Console.WriteLine("Host typeof(IAppModule) : " + typeof(IAppModule).Assembly.GetName().Name +
                              " @ " + typeof(IAppModule).Assembly.Location);

            var baseDir = AppContext.BaseDirectory;

            var configPath = Environment.GetEnvironmentVariable("PROBE_CONFIG");
            if (string.IsNullOrWhiteSpace(configPath)) configPath = Path.Combine(baseDir, "AppConfig.json");
            configPath = Path.GetFullPath(configPath);

            var webDir = Environment.GetEnvironmentVariable("PROBE_WEB_DIR");
            if (string.IsNullOrWhiteSpace(webDir)) webDir = Path.Combine(baseDir, "Web");
            webDir = Path.GetFullPath(webDir);

            Console.WriteLine("ConfigPath              : " + configPath + (File.Exists(configPath) ? " (exists)" : " (MISSING)"));
            Console.WriteLine("WebDir                  : " + webDir + (Directory.Exists(webDir) ? " (exists)" : " (MISSING)"));

            if (!File.Exists(configPath))
            {
                Console.WriteLine("[FATAL] config file not found");
                return 2;
            }

            bool enabled;
            try
            {
                enabled = ReadScadaWebEnabled(configPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FATAL] config parse failed: " + ex.GetType().Name + ": " + ex.Message);
                return 2;
            }

            Console.WriteLine("ScadaWeb.Enabled        : " + enabled);

            var dllPath = Path.Combine(webDir, "Probe.ScadaWeb.dll");
            var dllExists = File.Exists(dllPath);
            Console.WriteLine("ModuleDll               : " + dllPath + (dllExists ? " (exists)" : " (MISSING)"));

            // Premise for E: the module dir must NOT carry VM.*.dll
            var vmDllsInWebDir = Directory.Exists(webDir)
                ? Directory.GetFiles(webDir, "VM.*.dll").Select(p => Path.GetFileName(p)!).OrderBy(n => n).ToArray()
                : Array.Empty<string>();
            Console.WriteLine("VM.*.dll inside WebDir  : " +
                              (vmDllsInWebDir.Length == 0 ? "<none>" : string.Join(", ", vmDllsInWebDir)));

            var assembliesBefore = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name).Where(n => n != null).ToArray();
            Console.WriteLine("Probe.ScadaWeb loaded BEFORE load attempt: " +
                              assembliesBefore.Contains("Probe.ScadaWeb"));

            IAppModule? module = null;
            var loadAttempted = false;
            Exception? loadError = null;

            if (!enabled)
            {
                Console.WriteLine("[INFO] Enabled=false -> Assembly.LoadFrom is NOT called at all");
            }
            else if (!dllExists)
            {
                Console.WriteLine("[WARN] ScadaWeb module dll is missing, skip loading; host continues: " + dllPath);
            }
            else
            {
                loadAttempted = true;
                try
                {
                    var asm = Assembly.LoadFrom(dllPath);
                    Console.WriteLine("[INFO] Assembly.LoadFrom OK: " + asm.FullName);
                    Console.WriteLine("[INFO] loaded from       : " + asm.Location);

                    var type = asm.GetTypes()
                        .FirstOrDefault(t => typeof(IAppModule).IsAssignableFrom(t) && !t.IsAbstract);
                    Console.WriteLine("[INFO] module type       : " + (type?.FullName ?? "<none>"));

                    if (type != null)
                    {
                        module = (IAppModule?)Activator.CreateInstance(type);
                    }

                    if (module != null)
                    {
                        Console.WriteLine("[INFO] module.Name       : " + module.Name);
                        Console.WriteLine("[INFO] module type asm   : " + module.GetType().Assembly.GetName().Name);
                        module.Register(null!);
                        module.Initialize(null!, null!);
                    }
                }
                catch (Exception ex)
                {
                    loadError = ex;
                    Console.WriteLine("[ERROR] " + ex.GetType().FullName + ": " + ex.Message);
                    Console.WriteLine("---- exception detail ----");
                    Console.WriteLine(ex.ToString());
                    Console.WriteLine("--------------------------");
                }
            }

            // ---- AppDomain evidence ----
            var loadedNow = AppDomain.CurrentDomain.GetAssemblies();
            var probeAsm = loadedNow.FirstOrDefault(a => a.GetName().Name == "Probe.ScadaWeb");
            var scadaAsm = loadedNow.FirstOrDefault(a => a.GetName().Name == "VM.Scada");
            var coreAsm = loadedNow.FirstOrDefault(a => a.GetName().Name == "VM.Core");

            Console.WriteLine("[INFO] Probe.ScadaWeb loaded AFTER  : " + (probeAsm != null) +
                              (probeAsm != null ? " @ " + probeAsm.Location : string.Empty));
            Console.WriteLine("[INFO] VM.Core  loaded              : " + (coreAsm != null) +
                              (coreAsm != null ? " @ " + coreAsm.Location : string.Empty));
            Console.WriteLine("[INFO] VM.Scada loaded              : " + (scadaAsm != null) +
                              (scadaAsm != null ? " @ " + scadaAsm.Location : string.Empty));
            Console.WriteLine("[INFO] AppDomain VM.*/Probe.*       : " + string.Join(", ",
                              loadedNow.Select(a => a.GetName().Name)
                                       .Where(n => n != null && (n!.StartsWith("VM.", StringComparison.Ordinal) ||
                                                                 n.StartsWith("Probe.", StringComparison.Ordinal)))
                                       .OrderBy(n => n)));

            // ---- verdicts ----
            if (!enabled)
            {
                Check("A", probeAsm == null && !loadAttempted,
                    "Enabled=false -> Probe.ScadaWeb absent from AppDomain (" + (probeAsm == null) +
                    "), LoadFrom called=" + loadAttempted);

                Check("F", dllExists && probeAsm == null,
                    "Enabled=false while dll exists (" + dllExists + ") -> assembly not loaded (" +
                    (probeAsm == null) + ")");
            }
            else if (!dllExists)
            {
                var reachedEnd = true; // we are here, so nothing threw out of Main
                Check("D", loadError == null && reachedEnd,
                    "dll missing -> no exception, WARN printed, host still running (loadError=" +
                    (loadError == null ? "<none>" : loadError.GetType().Name) + ")");
            }
            else
            {
                Check("B", loadAttempted && loadError == null && module != null && !string.IsNullOrWhiteSpace(module.Name),
                    "load ok=" + (loadError == null) + ", module!=null=" + (module != null) +
                    ", Name=" + (module?.Name ?? "<null>"));

                var iface = module?.GetType().GetInterfaces()
                    .FirstOrDefault(i => i.FullName == typeof(IAppModule).FullName);
                var sameAsm = iface != null && ReferenceEquals(iface.Assembly, typeof(IAppModule).Assembly);
                Check("C", sameAsm,
                    "IAppModule on module type -> " + (iface?.Assembly.GetName().Name ?? "<null>") +
                    " @ " + (iface?.Assembly.Location ?? "<null>") +
                    " | host -> " + typeof(IAppModule).Assembly.GetName().Name +
                    " @ " + typeof(IAppModule).Assembly.Location +
                    " | same Assembly object=" + sameAsm);

                var coreFromBaseDir = coreAsm != null &&
                                      coreAsm.Location.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
                var scadaFromBaseDir = scadaAsm != null &&
                                       scadaAsm.Location.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
                Check("E", vmDllsInWebDir.Length == 0 && loadError == null && module != null &&
                           coreFromBaseDir && scadaFromBaseDir,
                    "WebDir has no VM.*.dll (" + (vmDllsInWebDir.Length == 0) + "), load ok=" + (loadError == null) +
                    ", VM.Core from baseDir=" + coreFromBaseDir + ", VM.Scada from baseDir=" + scadaFromBaseDir);
            }

            Console.WriteLine("[INFO] Host reached end of Main (no unhandled exception)");
            Console.WriteLine("SUMMARY: " + string.Join(" ", Verdicts.Select(kv => kv.Key + "=" + kv.Value)));
            return 0;
        }

        private static bool ReadScadaWebEnabled(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("ScadaWeb", out var section))
            {
                return false;
            }
            if (!section.TryGetProperty("Enabled", out var enabledProp))
            {
                return false;
            }
            return enabledProp.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(enabledProp.GetString(), out var b) && b,
                _ => false
            };
        }
    }
}
