using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SubZeroFramework.Analyzers;

internal static class ObservableInvocationHelpers
{
    internal static bool IsObservableSubscribeImplementation(ISymbol? containingSymbol, Compilation compilation)
    {
        if (containingSymbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (!string.Equals(methodSymbol.Name, "Subscribe", StringComparison.Ordinal))
        {
            return false;
        }

        if (!AnalyzerSymbolHelpers.ImplementsInterface(methodSymbol.ContainingType, compilation, AnalyzerSymbolHelpers.IObservableMetadataName))
        {
            return false;
        }

        if (!AnalyzerSymbolHelpers.ImplementsInterface(methodSymbol.ReturnType as INamedTypeSymbol, compilation, AnalyzerSymbolHelpers.IDisposableMetadataName)
            && !AnalyzerSymbolHelpers.IsType(methodSymbol.ReturnType, AnalyzerSymbolHelpers.IDisposableMetadataName))
        {
            return false;
        }

        return methodSymbol.Parameters.Any(parameter =>
            AnalyzerSymbolHelpers.IsType(parameter.Type, AnalyzerSymbolHelpers.IObserverMetadataName)
            || AnalyzerSymbolHelpers.ImplementsInterface(parameter.Type as INamedTypeSymbol, compilation, AnalyzerSymbolHelpers.IObserverMetadataName));
    }

    internal static bool IsObservableSubscribe(IInvocationOperation invocation, Compilation compilation)
    {
        if (!string.Equals(invocation.TargetMethod.Name, "Subscribe", StringComparison.Ordinal))
        {
            return false;
        }

        var receiver = GetReceiver(invocation);
        if (receiver?.Type is not INamedTypeSymbol receiverType)
        {
            return false;
        }

        return AnalyzerSymbolHelpers.IsType(receiverType.OriginalDefinition, AnalyzerSymbolHelpers.IObservableMetadataName)
            || AnalyzerSymbolHelpers.ImplementsInterface(receiverType, compilation, AnalyzerSymbolHelpers.IObservableMetadataName);
    }

    internal static bool HasObserveOnInReceiverChain(IInvocationOperation invocation)
        => HasInvocationInReceiverChain(GetReceiver(invocation), "ObserveOn");

    internal static bool HasDisposeWithInParentChain(IInvocationOperation invocation)
    {
        for (IOperation? current = invocation.Parent; current is not null; current = current.Parent)
        {
            if (current is IInvocationOperation parentInvocation
                && string.Equals(parentInvocation.TargetMethod.Name, "DisposeWith", StringComparison.Ordinal))
            {
                return true;
            }

            if (current is IExpressionStatementOperation
                or IVariableInitializerOperation
                or IReturnOperation
                or ISimpleAssignmentOperation
                or IUsingOperation)
            {
                return false;
            }
        }

        return false;
    }

    internal static bool IsForwardingSubscribeImplementation(IInvocationOperation invocation, Compilation compilation)
    {
        return invocation.SemanticModel is not null
            && IsObservableSubscribeImplementation(invocation.SemanticModel.GetEnclosingSymbol(invocation.Syntax.SpanStart), compilation);
    }

    private static bool HasInvocationInReceiverChain(IOperation? operation, string methodName)
    {
        operation = Unwrap(operation);
        if (operation is null)
        {
            return false;
        }

        if (operation is IInvocationOperation invocation)
        {
            if (string.Equals(invocation.TargetMethod.Name, methodName, StringComparison.Ordinal))
            {
                return true;
            }

            return HasInvocationInReceiverChain(GetReceiver(invocation), methodName);
        }

        return false;
    }

    private static IOperation? GetReceiver(IInvocationOperation invocation)
        => Unwrap(invocation.Instance ?? invocation.Arguments.FirstOrDefault()?.Value);

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }
}