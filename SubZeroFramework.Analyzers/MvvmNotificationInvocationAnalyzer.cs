using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SubZeroFramework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MvvmNotificationInvocationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        SubZeroDiagnosticDescriptors.AvoidDirectOnPropertyChanged,
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

        if (string.Equals(invocation.TargetMethod.Name, "NotifyCanExecuteChanged", StringComparison.Ordinal)
            && AnalyzerSymbolHelpers.ImplementsInterface(invocation.TargetMethod.ContainingType, context.Compilation, AnalyzerSymbolHelpers.RelayCommandInterfaceMetadataName))
        {
            context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.AvoidDirectNotifyCanExecuteChanged, invocation.Syntax.GetLocation()));
        }
    }
}