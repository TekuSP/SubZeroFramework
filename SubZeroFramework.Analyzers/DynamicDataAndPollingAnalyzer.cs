using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SubZeroFramework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DynamicDataAndPollingAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> PollingPropertyNames =
    [
        "IsPolling",
        "PollingInterval",
        "IsHardwareInfoPolling",
        "HardwareInfoPollingInterval",
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        SubZeroDiagnosticDescriptors.PollingStateMustBeMethodDriven,
        SubZeroDiagnosticDescriptors.CurrentTelemetryIdentityMustBePreserved,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (property.SetMethod is null || !PollingPropertyNames.Contains(property.Name))
        {
            return;
        }

        var containingType = property.ContainingType;
        if (!AnalyzerSymbolHelpers.IsType(containingType, AnalyzerSymbolHelpers.FrameworkDataProviderInterfaceMetadataName)
            && !AnalyzerSymbolHelpers.ImplementsInterface(containingType, context.Compilation, AnalyzerSymbolHelpers.FrameworkDataProviderInterfaceMetadataName))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.PollingStateMustBeMethodDriven, property.Locations[0], property.Name));
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!string.Equals(invocation.TargetMethod.Name, "Remove", StringComparison.Ordinal)
            && !string.Equals(invocation.TargetMethod.Name, "RemoveKey", StringComparison.Ordinal))
        {
            return;
        }

        if (context.ContainingSymbol is not IMethodSymbol containingMethod
            || !containingMethod.Parameters.Any(parameter => AnalyzerSymbolHelpers.IsCurrentTelemetryChangeSet(parameter.Type, context.Compilation)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(SubZeroDiagnosticDescriptors.CurrentTelemetryIdentityMustBePreserved, invocation.Syntax.GetLocation()));
    }
}