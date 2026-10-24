using System.Collections.Immutable;
using System.Linq;
using HKW.FastMember.Generater.SourceGenerator.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HKW.FastMember.Generater.SourceGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AccessorUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.IndirectReference, Diagnostics.DynamicInvocation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeReference,
            SyntaxKind.IdentifierName,
            SyntaxKind.GenericName
        );
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (
            AccessorModelBuilder.IsGenerated(
                invocation,
                context.SemanticModel,
                context.CancellationToken
            )
        )
            return;
        if (
            AccessorModelBuilder.IsDynamicEntry(
                invocation,
                context.SemanticModel,
                context.CancellationToken
            )
        )
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Diagnostics.DynamicInvocation,
                    invocation.GetLocation(),
                    invocation.Expression.ToString()
                )
            );
    }

    private static void AnalyzeReference(SyntaxNodeAnalysisContext context)
    {
        var name = (SimpleNameSyntax)context.Node;
        if (
            name.Identifier.ValueText != "Create"
            || AccessorModelBuilder.IsGenerated(
                name,
                context.SemanticModel,
                context.CancellationToken
            )
        )
            return;
        ExpressionSyntax expression = name;
        if (name.Parent is MemberAccessExpressionSyntax member && member.Name == name)
            expression = member;
        if (expression.Parent is InvocationExpressionSyntax call && call.Expression == expression)
            return;
        if (
            name.Ancestors()
                .OfType<InvocationExpressionSyntax>()
                .Any(n =>
                    n.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == "nameof"
                )
        )
            return;
        var info = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken);
        var method = info.Symbol as IMethodSymbol;
        if (!AccessorModelBuilder.IsEntry(method))
            method = info
                .CandidateSymbols.OfType<IMethodSymbol>()
                .FirstOrDefault(AccessorModelBuilder.IsEntry);
        if (!AccessorModelBuilder.IsEntry(method))
            return;
        context.ReportDiagnostic(
            Diagnostic.Create(
                Diagnostics.IndirectReference,
                expression.GetLocation(),
                method!.ToDisplayString()
            )
        );
    }
}
