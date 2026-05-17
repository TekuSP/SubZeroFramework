using Microsoft.CodeAnalysis;

namespace SubZeroFramework.Analyzers;

internal static class AnalyzerSymbolHelpers
{
    internal const string ObservableObjectMetadataName = "CommunityToolkit.Mvvm.ComponentModel.ObservableObject";
    internal const string ObservablePropertyAttributeMetadataName = "CommunityToolkit.Mvvm.ComponentModel.ObservablePropertyAttribute";
    internal const string RelayCommandInterfaceMetadataName = "CommunityToolkit.Mvvm.Input.IRelayCommand";
    internal const string IObservableMetadataName = "System.IObservable`1";
    internal const string IObserverMetadataName = "System.IObserver`1";
    internal const string IDisposableMetadataName = "System.IDisposable";
    internal const string CompositeDisposableMetadataName = "System.Reactive.Disposables.CompositeDisposable";
    internal const string FrameworkDataProviderInterfaceMetadataName = "SubZeroFramework.Services.IFrameworkDataProvider";
    internal const string IChangeSetMetadataName = "DynamicData.IChangeSet`2";
    internal const string CurrentTelemetryValueMetadataName = "SubZeroFramework.Models.CurrentTelemetryValue";
    internal const string TelemetryChannelIdMetadataName = "SubZeroFramework.Models.TelemetryChannelId";

    internal static bool HasAttribute(ISymbol symbol, string metadataName)
        => symbol.GetAttributes().Any(attribute => IsType(attribute.AttributeClass, metadataName));

    internal static bool DerivesFromOrEquals(INamedTypeSymbol? type, Compilation compilation, string metadataName)
    {
        var expectedType = compilation.GetTypeByMetadataName(metadataName);
        if (expectedType is null || type is null)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expectedType))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ImplementsInterface(INamedTypeSymbol? type, Compilation compilation, string metadataName)
    {
        var expectedType = compilation.GetTypeByMetadataName(metadataName);
        if (expectedType is null || type is null)
        {
            return false;
        }

        return type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, expectedType));
    }

    internal static bool IsType(ITypeSymbol? type, string metadataName)
        => type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).TrimStart("global::".ToCharArray()) == metadataName;

    internal static bool IsCurrentTelemetryChangeSet(ITypeSymbol? type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var expectedType = compilation.GetTypeByMetadataName(IChangeSetMetadataName);
        if (expectedType is null || !SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, expectedType))
        {
            return false;
        }

        return namedType.TypeArguments.Length == 2
            && IsType(namedType.TypeArguments[0], CurrentTelemetryValueMetadataName)
            && IsType(namedType.TypeArguments[1], TelemetryChannelIdMetadataName);
    }
}