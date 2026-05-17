using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SubZeroFramework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ObservableSubscriptionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        SubZeroDiagnosticDescriptors.ObservableSubscriptionMustObserveOn,
        SubZeroDiagnosticDescriptors.ObservableSubscriptionMustDisposeWith,
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
        if (!ObservableInvocationHelpers.IsObservableSubscribe(invocation, context.Compilation))
        {
            return;
        }

        if (ObservableInvocationHelpers.IsObservableSubscribeImplementation(context.ContainingSymbol, context.Compilation))
        {
            return;
        }

        if (ObservableInvocationHelpers.IsForwardingSubscribeImplementation(invocation, context.Compilation))
        {
            return;
        }

        if (!ObservableInvocationHelpers.HasObserveOnInReceiverChain(invocation))
        {
            context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.ObservableSubscriptionMustObserveOn, invocation.Syntax.GetLocation()));
        }

        if (!ObservableInvocationHelpers.HasDisposeWithInParentChain(invocation))
        {
            context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.ObservableSubscriptionMustDisposeWith, invocation.Syntax.GetLocation()));
        }
    }
}