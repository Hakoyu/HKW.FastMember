using System;
using System.Linq;
using System.Text;
using HKW.FastMember.Generater.SourceGenerator.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HKW.FastMember.Generater.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class AccessorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context
            .SyntaxProvider.CreateSyntaxProvider(
                (node, _) => node is InvocationExpressionSyntax,
                (syntax, cancellation) => AccessorModelBuilder.Analyze(syntax, cancellation)
            )
            .Where(result => result != null);
        context.RegisterSourceOutput(
            results,
            (output, result) =>
            {
                foreach (var diagnostic in result!.Diagnostics)
                    output.ReportDiagnostic(diagnostic);
            }
        );
        var models = results
            .Collect()
            .SelectMany(
                (items, _) =>
                    items
                        .Where(item => item!.Model != null)
                        .Select(item => item!.Model!)
                        .GroupBy(model => model.Identity, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => group.First())
                        .ToArray()
            )
            .WithTrackingName("AccessorModels");
        context.RegisterSourceOutput(
            models,
            (output, model) =>
                output.AddSource(
                    "Accessor_" + model.Identifier + ".g.cs",
                    SourceText.From(model.Source, Encoding.UTF8)
                )
        );
    }
}
