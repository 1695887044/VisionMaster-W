using System.Reflection;

var asm = typeof(ScottPlot.Plot).Assembly;

Console.WriteLine("=== types matching 'UserInput' / 'Interaction' ===");
foreach (var t in asm.GetTypes())
    if (t.Name.Contains("UserInput") || t.Name.Contains("InteractionProcessor") || t.Name.Contains("InputProcessor"))
    {
        Console.WriteLine($"--- {t.FullName} ---");
        foreach (var p in t.GetProperties())
            Console.WriteLine($"  P: {p.Name} : {p.PropertyType.Name} set={p.CanWrite}");
    }

Console.WriteLine("=== Plot.Interaction property ===");
var plot = asm.GetType("ScottPlot.Plot");
foreach (var p in plot.GetProperties())
    if (p.Name.Contains("Interaction") || p.Name.Contains("Input"))
        Console.WriteLine($"  P: {p.Name} : {p.PropertyType.Name}");
