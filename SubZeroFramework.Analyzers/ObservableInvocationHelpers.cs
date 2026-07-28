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

        return AnalyzerSymbolHelpers.IsOrImplementsInterface(receiverType, compilation, AnalyzerSymbolHelpers.IObservableMetadataName);
    }

    internal static bool HasObserveOnInReceiverChain(IInvocationOperation invocation)
        => HasInvocationInReceiverChain(GetReceiver(invocation), "ObserveOn")
            || MarshalsThroughDispatcher(invocation);

    /// <summary>
    /// True when the subscription marshals inside its handler instead of upstream of Subscribe.
    /// </summary>
    /// <remarks>
    /// The rule wants the target scheduler stated explicitly, and
    /// <c>.Select(x =&gt; Observable.FromAsync(() =&gt; dispatcherQueue.EnqueueAsync(...))).Concat()</c> —
    /// the idiom used throughout this repo where a handler is async and must not overlap itself — states it
    /// just as explicitly, only inside the handler. Requiring ObserveOn as well would marshal twice.
    ///
    /// Matched on the whole subscription statement rather than the receiver chain, because the dispatcher
    /// call is inside the lambda and so is not part of the chain leading to Subscribe.
    /// </remarks>
    private static bool MarshalsThroughDispatcher(IInvocationOperation invocation)
    {
        IOperation statement = invocation;
        while (statement.Parent is not null and not IExpressionStatementOperation)
        {
            statement = statement.Parent;
        }

        statement = statement.Parent ?? statement;

        var invocations = statement.Descendants().OfType<IInvocationOperation>().ToList();
        if (invocations.Any(candidate => IsDispatcherMarshal(candidate.TargetMethod)))
        {
            return true;
        }

        // Follow ONE hop into helpers on the same type. Marshalling is routinely factored into a small
        // private method (`UpdateSample(() => ...)` wrapping a TryEnqueue), and stopping at the call site
        // would report correct code. One level only: deeper chains are worth stating explicitly anyway, and
        // an unbounded walk would make analysis cost unpredictable.
        var containingType = (invocation.SemanticModel?.GetEnclosingSymbol(invocation.Syntax.SpanStart))?.ContainingType;
        foreach (var candidate in invocations)
        {
            var target = candidate.TargetMethod;
            if (containingType is null
                || !SymbolEqualityComparer.Default.Equals(target.ContainingType, containingType))
            {
                continue;
            }

            foreach (var reference in target.DeclaringSyntaxReferences)
            {
                var model = invocation.SemanticModel?.Compilation.GetSemanticModel(reference.SyntaxTree);
                var body = model?.GetOperation(reference.GetSyntax());
                if (body is not null
                    && body.Descendants().OfType<IInvocationOperation>().Any(inner => IsDispatcherMarshal(inner.TargetMethod)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsDispatcherMarshal(IMethodSymbol method)
        => string.Equals(method.Name, "EnqueueAsync", StringComparison.Ordinal)
            || string.Equals(method.Name, "TryEnqueue", StringComparison.Ordinal)
            || string.Equals(method.Name, "Post", StringComparison.Ordinal)
            || string.Equals(method.Name, "Send", StringComparison.Ordinal);

    /// <summary>
    /// True when the subscription's lifetime is owned by something — either DisposeWith, or storage in a
    /// member the containing type can dispose.
    /// </summary>
    /// <remarks>
    /// DisposeWith is the house style but not the only correct ownership. A SerialDisposable assignment
    /// (<c>_slot.Disposable = source.Subscribe(...)</c>) exists precisely to REPLACE a subscription, which
    /// DisposeWith cannot express — appending each replacement to a CompositeDisposable would accumulate
    /// dead subscriptions for the object's lifetime. A keyed registry assignment
    /// (<c>_perFan[index] = source.Subscribe(...)</c>) is the same story, and is already governed by the
    /// SZF0010/SZF0011 rules that require those values to be disposed on removal and in Dispose.
    ///
    /// Storing into a LOCAL is still reported: a local carries no ownership the type can act on.
    /// </remarks>
    internal static bool HasDisposeWithInParentChain(IInvocationOperation invocation)
    {
        for (IOperation? current = invocation.Parent; current is not null; current = current.Parent)
        {
            if (current is IInvocationOperation parentInvocation
                && string.Equals(parentInvocation.TargetMethod.Name, "DisposeWith", StringComparison.Ordinal))
            {
                return true;
            }

            // Storing the subscription in a member hands its lifetime to the containing type.
            if (current is ISimpleAssignmentOperation assignment)
            {
                return assignment.Target is IFieldReferenceOperation
                    or IPropertyReferenceOperation
                    or IArrayElementReferenceOperation;
            }

            // Passing it straight to a disposable container is ownership too, e.g. _subscriptions.Add(...).
            if (current is IArgumentOperation { Parent: IInvocationOperation container }
                && string.Equals(container.TargetMethod.Name, "Add", StringComparison.Ordinal))
            {
                return true;
            }

            // Returning the subscription hands ownership to the caller, which is what a
            // `private IDisposable SubscribeX() => source.Subscribe(...)` factory is for.
            if (current is IReturnOperation)
            {
                return true;
            }

            // `using var subscription = source.Subscribe(...)` disposes deterministically at scope exit.
            if (current is IUsingOperation or IUsingDeclarationOperation)
            {
                return true;
            }

            // NOTE: a variable initializer is NOT a stopping point. `using var x = source.Subscribe(...)`
            // nests the initializer inside the using declaration, so bailing out at the initializer would
            // report the one form that disposes most reliably of all.
            if (current is IExpressionStatementOperation)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the subscription is lexically inside an <c>Observable.Create</c> factory.
    /// </summary>
    /// <remarks>
    /// Both the ObserveOn and DisposeWith rules exist to protect a CONSUMER of a stream, and neither applies
    /// to the code that builds one:
    ///
    /// Disposal is already correct by construction — the Create callback returns the disposable that tears
    /// these subscriptions down when the last subscriber goes away. Rewriting that to DisposeWith would mean
    /// tying a per-subscription lifetime to a field on the factory, which outlives it.
    ///
    /// Scheduling is the consumer's decision. A shared factory that called ObserveOn would impose one
    /// scheduler on every downstream subscriber, and for a stream that several view models observe on the UI
    /// thread that is worse than leaving it alone.
    /// </remarks>
    internal static bool IsInsideObservableFactory(IInvocationOperation invocation)
    {
        for (IOperation? current = invocation.Parent; current is not null; current = current.Parent)
        {
            if (current is IInvocationOperation parent
                && string.Equals(parent.TargetMethod.Name, "Create", StringComparison.Ordinal)
                && AnalyzerSymbolHelpers.IsType(parent.TargetMethod.ContainingType, ObservableStaticMetadataName))
            {
                return true;
            }
        }

        return false;
    }

    private const string ObservableStaticMetadataName = "System.Reactive.Linq.Observable";

    internal static bool IsForwardingSubscribeImplementation(IInvocationOperation invocation, Compilation compilation)
    {
        return invocation.SemanticModel is not null
            && IsObservableSubscribeImplementation(invocation.SemanticModel.GetEnclosingSymbol(invocation.Syntax.SpanStart), compilation);
    }

    /// <summary>True when any link in the receiver chain calls one of <paramref name="methodNames"/>.</summary>
    internal static bool HasAnyInvocationInReceiverChain(IInvocationOperation invocation, ICollection<string> methodNames)
    {
        for (IOperation? current = GetChainReceiver(invocation); current is not null; current = GetChainReceiver(current))
        {
            if (current is IInvocationOperation chained && methodNames.Contains(chained.TargetMethod.Name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The next link up a fluent observable chain. Handles both instance calls and extension calls, whose
    /// receiver is the first argument rather than the instance.
    /// </summary>
    internal static IOperation? GetChainReceiver(IOperation? operation)
        => operation is IInvocationOperation invocation ? GetReceiver(invocation) : null;

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