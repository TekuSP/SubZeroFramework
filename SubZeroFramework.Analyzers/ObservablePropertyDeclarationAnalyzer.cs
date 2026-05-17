using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SubZeroFramework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ObservablePropertyDeclarationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        SubZeroDiagnosticDescriptors.ObservablePropertyMustBePartialProperty,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (!AnalyzerSymbolHelpers.HasAttribute(field, AnalyzerSymbolHelpers.ObservablePropertyAttributeMetadataName))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.ObservablePropertyMustBePartialProperty, field.Locations[0]));
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (!AnalyzerSymbolHelpers.HasAttribute(property, AnalyzerSymbolHelpers.ObservablePropertyAttributeMetadataName))
        {
            return;
        }

        var syntax = property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken) as PropertyDeclarationSyntax;
        if (syntax is not null && syntax.Modifiers.Any(modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.ObservablePropertyMustBePartialProperty, property.Locations[0]));
    }
}