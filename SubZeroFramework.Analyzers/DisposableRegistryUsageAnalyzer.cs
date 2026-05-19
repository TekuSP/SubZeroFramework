using System.Collections.Immutable;
using System.Collections.Concurrent;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SubZeroFramework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DisposableRegistryUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        SubZeroDiagnosticDescriptors.DisposableRegistryMustDisposeRemainingValues,
        SubZeroDiagnosticDescriptors.DisposableRegistryRemoveMustDisposeValue,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolStartAction(AnalyzeNamedTypeStart, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedTypeStart(SymbolStartAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!AnalyzerSymbolHelpers.ImplementsInterface(type, context.Compilation, AnalyzerSymbolHelpers.IDisposableMetadataName))
        {
            return;
        }

        var disposableRegistries = GetDisposableRegistries(type, context.Compilation)
            .ToImmutableHashSet(SymbolEqualityComparer.Default);

        if (disposableRegistries.IsEmpty)
        {
            return;
        }

        var disposedInDispose = new ConcurrentDictionary<ISymbol, byte>(SymbolEqualityComparer.Default);

        context.RegisterSyntaxNodeAction(
            syntaxContext => AnalyzeDisposeMethod(syntaxContext, disposableRegistries, disposedInDispose),
            SyntaxKind.MethodDeclaration);

        context.RegisterSyntaxNodeAction(
            syntaxContext => AnalyzeRemoveInvocation(syntaxContext, disposableRegistries),
            SyntaxKind.InvocationExpression);

        context.RegisterSymbolEndAction(endContext =>
        {
            foreach (var registry in disposableRegistries)
            {
                if (disposedInDispose.ContainsKey(registry))
                {
                    continue;
                }

                endContext.ReportDiagnostic(Diagnostic.Create(
                    SubZeroDiagnosticDescriptors.DisposableRegistryMustDisposeRemainingValues,
                    registry.Locations[0],
                    registry.Name));
            }
        });
    }

    private static ImmutableArray<ISymbol> GetDisposableRegistries(INamedTypeSymbol type, Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<ISymbol>();

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol { IsStatic: false } field when AnalyzerSymbolHelpers.IsDisposableRegistry(field.Type, compilation):
                    builder.Add(field);
                    break;
                case IPropertySymbol { IsStatic: false } property when AnalyzerSymbolHelpers.IsDisposableRegistry(property.Type, compilation):
                    builder.Add(property);
                    break;
            }
        }

        return builder.ToImmutable();
    }

    private static void AnalyzeDisposeMethod(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<ISymbol> disposableRegistries,
        ConcurrentDictionary<ISymbol, byte> disposedInDispose)
    {
        if (context.Node is not MethodDeclarationSyntax methodSyntax
            || context.SemanticModel.GetDeclaredSymbol(methodSyntax, context.CancellationToken) is not IMethodSymbol methodSymbol
            || !IsDisposeMethod(methodSymbol))
        {
            return;
        }

        foreach (var foreachSyntax in methodSyntax.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (foreachSyntax.Ancestors().Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            {
                continue;
            }

            var registry = GetEnumeratedRegistrySymbol(foreachSyntax.Expression, context.SemanticModel, context.CancellationToken);
            if (registry is null || !disposableRegistries.Contains(registry))
            {
                continue;
            }

            var loopVariable = context.SemanticModel.GetDeclaredSymbol(foreachSyntax, context.CancellationToken);
            if (loopVariable is null || !ContainsDisposeCallForSymbol(foreachSyntax.Statement, context.SemanticModel, loopVariable, context.CancellationToken))
            {
                continue;
            }

            disposedInDispose.TryAdd(registry, 0);
        }
    }

    private static void AnalyzeRemoveInvocation(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<ISymbol> disposableRegistries)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || invocation.Ancestors().Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
        {
            return;
        }

        if (!TryGetDisposableRegistryRemoveInvocation(invocation, context.SemanticModel, disposableRegistries, context.CancellationToken, out var registry, out var removedValueSymbol))
        {
            return;
        }

        if (IsRemovedValueDisposed(invocation, context.SemanticModel, removedValueSymbol, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            SubZeroDiagnosticDescriptors.DisposableRegistryRemoveMustDisposeValue,
            invocation.GetLocation(),
            registry.Name));
    }

    private static bool TryGetDisposableRegistryRemoveInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ImmutableHashSet<ISymbol> disposableRegistries,
        CancellationToken cancellationToken,
        out ISymbol registry,
        out ISymbol removedValueSymbol)
    {
        registry = null!;
        removedValueSymbol = null!;

        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol methodSymbol
            || !string.Equals(methodSymbol.Name, "Remove", StringComparison.Ordinal)
            || !methodSymbol.Parameters.Any(parameter => parameter.RefKind == RefKind.Out))
        {
            return false;
        }

        if (!TryGetReceiverSymbol(invocation.Expression, semanticModel, cancellationToken, out var receiverSymbol)
            || !disposableRegistries.Contains(receiverSymbol))
        {
            return false;
        }

        var outArgument = invocation.ArgumentList.Arguments.FirstOrDefault(argument => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword));
        if (outArgument is null || !TryGetOutValueSymbol(outArgument.Expression, semanticModel, cancellationToken, out removedValueSymbol))
        {
            return false;
        }

        registry = receiverSymbol;
        return true;
    }

    private static bool IsRemovedValueDisposed(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ISymbol removedValueSymbol,
        CancellationToken cancellationToken)
    {
        var conditional = invocation.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault(statement => statement.Condition.Span.Contains(invocation.Span));
        if (conditional is not null)
        {
            return ContainsDisposeCallForSymbol(conditional.Statement, semanticModel, removedValueSymbol, cancellationToken);
        }

        var currentStatement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (currentStatement?.Parent is not BlockSyntax block)
        {
            return false;
        }

        var currentIndex = block.Statements.IndexOf(currentStatement);
        for (var index = currentIndex + 1; index < block.Statements.Count; index++)
        {
            var statement = block.Statements[index];
            if (ContainsDisposeCallForSymbol(statement, semanticModel, removedValueSymbol, cancellationToken))
            {
                return true;
            }

            if (statement is ReturnStatementSyntax or ThrowStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax)
            {
                break;
            }
        }

        return false;
    }

    private static ISymbol? GetEnumeratedRegistrySymbol(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess
            || !string.Equals(memberAccess.Name.Identifier.ValueText, "Values", StringComparison.Ordinal))
        {
            return null;
        }

        return semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
    }

    private static bool ContainsDisposeCallForSymbol(
        SyntaxNode scope,
        SemanticModel semanticModel,
        ISymbol targetSymbol,
        CancellationToken cancellationToken)
    {
        foreach (var invocation in scope.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Ancestors().Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            {
                continue;
            }

            if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol methodSymbol
                || !string.Equals(methodSymbol.Name, "Dispose", StringComparison.Ordinal)
                || methodSymbol.Parameters.Length != 0)
            {
                continue;
            }

            if (!TryGetReceiverSymbol(invocation.Expression, semanticModel, cancellationToken, out var receiverSymbol))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(receiverSymbol, targetSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetReceiverSymbol(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken, out ISymbol symbol)
    {
        symbol = null!;

        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                symbol = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol!;
                return symbol is not null;
            case MemberBindingExpressionSyntax:
                if (expression.Parent is InvocationExpressionSyntax { Parent: ConditionalAccessExpressionSyntax conditionalAccess })
                {
                    symbol = semanticModel.GetSymbolInfo(conditionalAccess.Expression, cancellationToken).Symbol!;
                    return symbol is not null;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryGetOutValueSymbol(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken, out ISymbol symbol)
    {
        symbol = null!;

        switch (expression)
        {
            case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation }:
                symbol = semanticModel.GetDeclaredSymbol(designation, cancellationToken)!;
                return symbol is not null;
            default:
                symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol!;
                return symbol is not null;
        }
    }

    private static bool IsDisposeMethod(IMethodSymbol method)
        => string.Equals(method.Name, "Dispose", StringComparison.Ordinal)
            && !method.IsStatic
            && method.Parameters.Length == 0;
}