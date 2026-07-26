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

    /// <summary>
    /// Marker for "this compilation has a UI thread". Present only in the app head.
    /// </summary>
    private const string XamlApplicationMetadataName = "Microsoft.UI.Xaml.Application";

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationStart =>
        {
            // ObserveOn exists to marshal onto the UI thread. The service and Core have no UI thread at all,
            // so requiring it there is meaningless — it would only push work onto an arbitrary scheduler and
            // make the reader think a dispatcher was involved. Resolved once per compilation rather than
            // per call site, and by type rather than by assembly name so a rename cannot silently disable it.
            var hasUiThread = compilationStart.Compilation.GetTypeByMetadataName(XamlApplicationMetadataName) is not null;

            compilationStart.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, hasUiThread),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, bool hasUiThread)
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

        // Stream factories build streams rather than consume them; see IsInsideObservableFactory.
        if (ObservableInvocationHelpers.IsInsideObservableFactory(invocation))
        {
            return;
        }

        if (hasUiThread && !ObservableInvocationHelpers.HasObserveOnInReceiverChain(invocation))
        {
            context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.ObservableSubscriptionMustObserveOn, invocation.Syntax.GetLocation()));
        }

        if (!ObservableInvocationHelpers.HasDisposeWithInParentChain(invocation))
        {
            context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.ObservableSubscriptionMustDisposeWith, invocation.Syntax.GetLocation()));
        }
    }
}