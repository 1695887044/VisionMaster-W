using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

int bad = 0;
foreach (var f in args)
{
    var text = File.ReadAllText(f);
    var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview), f);
    var errs = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    Console.WriteLine($"{Path.GetFileName(f)}: syntaxErrors={errs.Count}");
    foreach (var e in errs)
        Console.WriteLine($"   {e.Id} {e.GetMessage()} @line {e.Location.GetLineSpan().StartLinePosition.Line + 1}");
    bad += errs.Count;
}
Console.WriteLine(bad == 0 ? "SYNTAX: OK" : $"SYNTAX: {bad} error(s)");
Environment.ExitCode = bad == 0 ? 0 : 1;