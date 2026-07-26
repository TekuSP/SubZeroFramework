using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SubZeroFramework.Analyzers;

/// <summary>
/// SZF0013: a subscription whose chain starts at a high-frequency telemetry source must apply a rate-limiting
/// operator before Subscribe.
/// </summary>
/// <remarks>
/// These streams tick on every poll, and the poll interval is user-configurable — a subscription that does
/// per-tick work inherits whatever cadence the user picked. That is the difference between a chart updating
/// once a second and the same chart rebuilding its geometry a hundred times a second on the UI thread.
///
/// Scoped deliberately narrowly. Only members declared on <see cref="IFrameworkDataProvider"/> (or a type
/// implementing it) that are known to be per-poll count as sources, so command streams, one-shot status
/// changes and UI events are not flagged. Aggregating operators are accepted as well as the obvious
/// rate-limiters: a chain that already reduces to one value per window does not also need throttling.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TelemetrySubscriptionRateLimitAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Per-poll members, on the service-side provider and on the app-side gRPC clients alike. History and
    /// series members are included because they re-emit on every append.
    /// </summary>
    /// <remarks>
    /// Members that change only on a user action or a hardware event — fan capabilities, module inventory,
    /// configuration, unit preferences — are deliberately absent. Flagging those would be noise, and noisy
    /// rules get suppressed wholesale, taking the useful diagnostics with them.
    /// </remarks>
    private static readonly HashSet<string> TelemetrySourceNames =
    [
        // IFrameworkDataProvider (service side).
        "SystemStatus",
        "FlashSnapshots",
        "PowerSnapshots",
        "ThermalSnapshots",
        "HardwareInfoSnapshots",
        "ConnectSystemStatusHistory",
        "ConnectFlashHistory",
        "ConnectPowerHistory",
        "ConnectThermalHistory",
        "ConnectHardwareInfoHistory",
        "ConnectTelemetrySeries",
        "ConnectTemperatureSeries",
        "ConnectFanSpeedSeries",
        "ConnectBatteryChargeSeries",
        "ConnectBatteryPresentRateSeries",
        "ConnectBatteryPresentVoltageSeries",
        "ConnectCurrentTelemetryValues",
        "ConnectFanStates",

        // I*Client (app side) — the same telemetry after a gRPC hop, and the side where a per-tick
        // subscription lands on the UI thread.
        "WatchStatus",
        "WatchCurrentTelemetryValues",
        "WatchTelemetrySeries",
        "WatchTemperatures",
        "WatchTemperatureHistory",
        "WatchFans",
        "WatchFanHistory",
        "WatchFanStates",
        "WatchFanControlStates",
        "WatchBatteries",
        "WatchBatteryHistory",
        "WatchHardwareInfo",
        "WatchHardwareInfoHistory",
    ];

    /// <summary>
    /// Operators that bound how often the subscriber runs.
    /// </summary>
    /// <remarks>
    /// Split by stream shape, because the correct choice differs and picking the wrong one is a correctness
    /// bug rather than a style problem.
    ///
    /// A change set is a DELTA. Sample and Throttle emit the most recent item and DISCARD the rest, so
    /// applying them to a change-set stream permanently loses whichever adds and removes fell in the gap —
    /// DynamicData deliberately ships no Sample/Throttle for change sets. Batch coalesces instead, which is
    /// why it is the only right answer there.
    ///
    /// A snapshot stream carries the whole current value every time, so dropping intermediate items is
    /// exactly the intended behaviour and Sample/Throttle are correct.
    ///
    /// DistinctUntilChanged is absent from both lists: it drops repeats but does nothing for a continuously
    /// varying value such as a temperature, which is the case this rule exists for.
    /// </remarks>
    private static readonly HashSet<string> ChangeSetRateLimitingOperators =
    [
        "Batch",
        "BatchIf",
        "BufferInitial",
    ];

    private static readonly HashSet<string> SnapshotRateLimitingOperators =
    [
        "Sample",
        "Throttle",
        "Buffer",
        "Window",
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        SubZeroDiagnosticDescriptors.TelemetrySubscriptionMustRateLimit,
    ];

    /// <summary>Marker for "this compilation has a UI thread". Present only in the app head.</summary>
    private const string XamlApplicationMetadataName = "Microsoft.UI.Xaml.Application";

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationStart =>
        {
            // Scoped to the UI head for the same reason as SZF0004. The cost this rule bounds is per-tick UI
            // work. Service-side streams are the opposite case: the fan-control state store is authoritative
            // state that the curve worker reads, and delaying it would add latency to a safety path to save
            // work that is not being done. Service consumers that DO need pacing already sample explicitly.
            if (compilationStart.Compilation.GetTypeByMetadataName(XamlApplicationMetadataName) is null)
            {
                return;
            }

            compilationStart.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!ObservableInvocationHelpers.IsObservableSubscribe(invocation, context.Compilation))
        {
            return;
        }

        // A type's own Subscribe implementation is plumbing, not a consumer.
        if (ObservableInvocationHelpers.IsObservableSubscribeImplementation(context.ContainingSymbol, context.Compilation)
            || ObservableInvocationHelpers.IsForwardingSubscribeImplementation(invocation, context.Compilation))
        {
            return;
        }

        var source = FindTelemetrySourceInChain(invocation, context.Compilation);
        if (source is not (string sourceName, bool isChangeSet))
        {
            return;
        }

        var accepted = isChangeSet ? ChangeSetRateLimitingOperators : SnapshotRateLimitingOperators;

        if (ObservableInvocationHelpers.HasAnyInvocationInReceiverChain(invocation, accepted))
        {
            return;
        }

        // Naming the right operator matters: recommending Sample on a change set would lose changes.
        var advice = isChangeSet
            ? "Batch(...) — NOT Sample/Throttle, which would drop change sets and lose adds or removes"
            : "a rate-limiting operator (Sample, Throttle, Buffer or Window)";

        context.ReportDiagnostic(Diagnostic.Create(
            SubZeroDiagnosticDescriptors.TelemetrySubscriptionMustRateLimit,
            invocation.Syntax.GetLocation(),
            sourceName,
            advice));
    }

    /// <summary>
    /// Walks the receiver chain looking for a per-poll provider member, returning its name so the diagnostic
    /// can say which stream it means, and whether that SOURCE emits change sets.
    /// </summary>
    /// <remarks>
    /// The shape is read off the source rather than off the Subscribe. By the time a chain reaches Subscribe
    /// it has usually been projected — <c>.Select(...).Concat()</c> over an async handler leaves an
    /// <c>IObservable&lt;Unit&gt;</c> — so inspecting the subscribed element type would report every such
    /// chain as a snapshot stream and then recommend Sample, which is the one operator that must not be used
    /// on the change sets flowing through it.
    /// </remarks>
    private static (string Name, bool IsChangeSet)? FindTelemetrySourceInChain(IInvocationOperation invocation, Compilation compilation)
    {
        for (IOperation? current = ObservableInvocationHelpers.GetChainReceiver(invocation);
             current is not null;
             current = ObservableInvocationHelpers.GetChainReceiver(current))
        {
            ISymbol? member = current switch
            {
                IInvocationOperation chained => chained.TargetMethod,
                IPropertyReferenceOperation property => property.Property,
                _ => null,
            };

            if (member is null || !IsTelemetrySource(member, compilation))
            {
                continue;
            }

            var sourceType = member switch
            {
                IMethodSymbol method => method.ReturnType,
                IPropertySymbol property => property.Type,
                _ => null,
            };

            return (member.Name, EmitsChangeSets(sourceType, compilation));
        }

        return null;
    }

    /// <summary>
    /// True when the type is an <c>IObservable&lt;IChangeSet&lt;...&gt;&gt;</c>, which decides whether Batch
    /// or Sample/Throttle is the correct operator.
    /// </summary>
    private static bool EmitsChangeSets(ITypeSymbol? observableType, Compilation compilation)
    {
        var changeSetType = compilation.GetTypeByMetadataName(AnalyzerSymbolHelpers.IChangeSetMetadataName);
        if (changeSetType is null
            || observableType is not INamedTypeSymbol { TypeArguments.Length: 1 } named)
        {
            return false;
        }

        return named.TypeArguments[0] is INamedTypeSymbol element
            && SymbolEqualityComparer.Default.Equals(element.OriginalDefinition, changeSetType);
    }

    private static bool IsTelemetrySource(ISymbol member, Compilation compilation)
    {
        if (!TelemetrySourceNames.Contains(member.Name))
        {
            return false;
        }

        // Gate on the declaring type so an unrelated member that happens to share a name is not flagged.
        var containingType = member.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        return AnalyzerSymbolHelpers.IsType(containingType, AnalyzerSymbolHelpers.FrameworkDataProviderInterfaceMetadataName)
            || AnalyzerSymbolHelpers.ImplementsInterface(containingType, compilation, AnalyzerSymbolHelpers.FrameworkDataProviderInterfaceMetadataName)
            || IsTelemetryClientType(containingType);
    }

    /// <summary>
    /// True for the app's <c>SubZeroFramework.Services.I*Client</c> telemetry interfaces and their
    /// implementations. Matched by shape rather than by a metadata-name constant because these live in the
    /// app project, which the service and Core compilations do not reference — a hard type lookup would
    /// silently resolve to null there and disable the rule.
    /// </summary>
    private static bool IsTelemetryClientType(INamedTypeSymbol type)
        => IsTelemetryClientInterface(type) || type.AllInterfaces.Any(IsTelemetryClientInterface);

    private static bool IsTelemetryClientInterface(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Interface
            && type.Name.Length > 7
            && type.Name[0] == 'I'
            && type.Name.EndsWith("Client", StringComparison.Ordinal)
            && string.Equals(type.ContainingNamespace?.ToDisplayString(), "SubZeroFramework.Services", StringComparison.Ordinal);
}
