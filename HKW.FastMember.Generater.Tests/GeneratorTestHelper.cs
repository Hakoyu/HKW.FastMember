using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using HKW.FastMember.Generater.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HKW.FastMember.Generater.Tests;

internal static class GeneratorTestHelper
{
    internal static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);
    private static readonly ImmutableArray<MetadataReference> References = (
        (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!
    )
        .Split(Path.PathSeparator)
        .Append(typeof(TypeAccessor).Assembly.Location)
        .Append(typeof(Abstractions.TypeAccessor).Assembly.Location)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToImmutableArray<MetadataReference>();

    internal static CSharpCompilation Compilation(
        string source,
        string? name = null,
        IEnumerable<MetadataReference>? extra = null
    ) =>
        CSharpCompilation.Create(
            name ?? "Consumer_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Consumer.cs")],
            References.AddRange(extra ?? []),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

    internal static GeneratorDriver Driver(AnalyzerConfigOptionsProvider? options = null) =>
        CSharpGeneratorDriver.Create(
            generators: [new AccessorGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            optionsProvider: options,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );

    internal static async Task<Result> Run(
        string source,
        string? name = null,
        IEnumerable<MetadataReference>? extra = null,
        AnalyzerConfigOptionsProvider? options = null
    )
    {
        var input = Compilation(source, name, extra);
        if (options != null)
            input = input.WithOptions(
                input.Options.WithSyntaxTreeOptionsProvider(new DiagnosticOptions(options))
            );
        var driver = Driver(options)
            .RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new AccessorUsageAnalyzer());
        var analyzerOptions = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            options ?? new Configuration()
        );
        var analyzerDiagnostics = await output
            .WithAnalyzers(analyzers, analyzerOptions)
            .GetAnalyzerDiagnosticsAsync();
        return new Result((CSharpCompilation)output, driver, diagnostics, analyzerDiagnostics);
    }

    internal static void AssertCompiles(Result result)
    {
        var errors = result
            .GeneratorDiagnostics.Concat(result.AnalyzerDiagnostics)
            .Concat(result.Output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.AreEqual(
            0,
            errors.Length,
            string.Join(Environment.NewLine, errors.Select(e => e.ToString()))
        );
    }

    internal static byte[] Emit(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    internal static object? Execute(Result result, string type = "Entry", string method = "Test")
    {
        AssertCompiles(result);
        var context = new TestLoadContext();
        try
        {
            using var stream = new MemoryStream(Emit(result.Output));
            var assembly = context.LoadFromStream(stream);
            return assembly.GetType(type)!.GetMethod(method)!.Invoke(null, null);
        }
        finally
        {
            context.Unload();
        }
    }

    internal sealed record Result(
        CSharpCompilation Output,
        GeneratorDriver Driver,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> AnalyzerDiagnostics
    )
    {
        internal Diagnostic[] Diagnostics =>
            GeneratorDiagnostics.Concat(AnalyzerDiagnostics).ToArray();
    }

    internal sealed class TestLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            assemblyName.Name == typeof(TypeAccessor).Assembly.GetName().Name
                ? typeof(TypeAccessor).Assembly
            : assemblyName.Name == typeof(Abstractions.TypeAccessor).Assembly.GetName().Name
                ? typeof(Abstractions.TypeAccessor).Assembly
            : null;
    }

    private sealed class DiagnosticOptions(AnalyzerConfigOptionsProvider options)
        : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(
            SyntaxTree tree,
            CancellationToken cancellationToken
        ) => GeneratedKind.Unknown;

        public override bool TryGetDiagnosticValue(
            SyntaxTree tree,
            string diagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity
        ) => Read(options.GetOptions(tree), diagnosticId, out severity);

        public override bool TryGetGlobalDiagnosticValue(
            string diagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity
        ) => Read(options.GlobalOptions, diagnosticId, out severity);

        private static bool Read(
            AnalyzerConfigOptions options,
            string id,
            out ReportDiagnostic severity
        )
        {
            var found = options.TryGetValue("dotnet_diagnostic." + id + ".severity", out var text);
            severity = text switch
            {
                "error" => ReportDiagnostic.Error,
                "warning" => ReportDiagnostic.Warn,
                "none" => ReportDiagnostic.Suppress,
                "suggestion" => ReportDiagnostic.Info,
                _ => ReportDiagnostic.Default,
            };
            return found;
        }
    }

    internal sealed class Configuration(IReadOnlyDictionary<string, string>? values = null)
        : AnalyzerConfigOptionsProvider
    {
        private readonly Options _options = new(values ?? new Dictionary<string, string>());
        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;

        private sealed class Options(IReadOnlyDictionary<string, string> values)
            : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) =>
                values.TryGetValue(key, out value!);
        }
    }
}
