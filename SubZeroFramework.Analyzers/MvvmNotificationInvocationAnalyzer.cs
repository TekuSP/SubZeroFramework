using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SubZeroFramework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MvvmNotificationInvocationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        SubZeroDiagnosticDescriptors.AvoidDirectOnPropertyChanged,
        SubZeroDiagnosticDescriptors.AvoidDirectPropertyChangedEventInvocation,
        SubZeroDiagnosticDescriptors.AvoidDirectNotifyCanExecuteChanged,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (string.Equals(invocation.TargetMethod.Name, "OnPropertyChanged", StringComparison.Ordinal)
            && AnalyzerSymbolHelpers.DerivesFromOrEquals(context.ContainingSymbol?.ContainingType, context.Compilation, AnalyzerSymbolHelpers.ObservableObjectMetadataName))
        {
            context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.AvoidDirectOnPropertyChanged, invocation.Syntax.GetLocation()));
            return;
        }

        if (IsDirectPropertyChangedEventInvocation(invocation))
        {
            context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.AvoidDirectPropertyChangedEventInvocation, invocation.Syntax.GetLocation()));
            return;
        }

        if (string.Equals(invocation.TargetMethod.Name, "NotifyCanExecuteChanged", StringComparison.Ordinal)
            && AnalyzerSymbolHelpers.ImplementsInterface(invocation.TargetMethod.ContainingType, context.Compilation, AnalyzerSymbolHelpers.RelayCommandInterfaceMetadataName))
        {
            context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.AvoidDirectNotifyCanExecuteChanged, invocation.Syntax.GetLocation()));
        }
    }

    private static bool IsDirectPropertyChangedEventInvocation(IInvocationOperation invocation)
    {
        if (!string.Equals(invocation.TargetMethod.Name, "Invoke", StringComparison.Ordinal)
            || !AnalyzerSymbolHelpers.IsType(invocation.TargetMethod.ContainingType, AnalyzerSymbolHelpers.PropertyChangedEventHandlerMetadataName))
        {
            return false;
        }

        var eventSymbol = GetInvokedEvent(invocation);
        return eventSymbol is not null
            && string.Equals(eventSymbol.Name, "PropertyChanged", StringComparison.Ordinal)
            && AnalyzerSymbolHelpers.IsType(eventSymbol.Type, AnalyzerSymbolHelpers.PropertyChangedEventHandlerMetadataName);
    }

    private static IEventSymbol? GetInvokedEvent(IInvocationOperation invocation)
    {
        if (invocation.SemanticModel is null || invocation.Syntax is not InvocationExpressionSyntax syntax)
        {
            return null;
        }

        return syntax.Expression switch
        {
            IdentifierNameSyntax identifier => invocation.SemanticModel.GetSymbolInfo(identifier).Symbol as IEventSymbol,
            MemberAccessExpressionSyntax memberAccess => invocation.SemanticModel.GetSymbolInfo(memberAccess.Expression).Symbol as IEventSymbol,
            MemberBindingExpressionSyntax => invocation.Syntax.Parent is ConditionalAccessExpressionSyntax conditionalAccess
                ? invocation.SemanticModel.GetSymbolInfo(conditionalAccess.Expression).Symbol as IEventSymbol
                : null,
            _ => null,
        };
    }
}