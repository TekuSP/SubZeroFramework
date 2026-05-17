using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SubZeroFramework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompositeDisposableUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        SubZeroDiagnosticDescriptors.ObservableSubscriptionsNeedCompositeDisposable,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!AnalyzerSymbolHelpers.ImplementsInterface(type, context.Compilation, AnalyzerSymbolHelpers.IDisposableMetadataName))
        {
            return;
        }

        if (!ContainsObservableSubscription(type, context))
        {
            return;
        }

        if (OwnsCompositeDisposable(type))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.ObservableSubscriptionsNeedCompositeDisposable, type.Locations[0], type.Name));
    }

    private static bool ContainsObservableSubscription(INamedTypeSymbol type, SymbolAnalysisContext context)
    {
        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax(context.CancellationToken);
            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Ancestors().Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                {
                    continue;
                }

                var methodName = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                    MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    _ => string.Empty,
                };

                if (string.Equals(methodName, "Subscribe", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool OwnsCompositeDisposable(INamedTypeSymbol type)
        => type.GetMembers().Any(member => member switch
        {
            IFieldSymbol field => AnalyzerSymbolHelpers.IsType(field.Type, AnalyzerSymbolHelpers.CompositeDisposableMetadataName),
            IPropertySymbol property => AnalyzerSymbolHelpers.IsType(property.Type, AnalyzerSymbolHelpers.CompositeDisposableMetadataName),
            _ => false,
        });
}