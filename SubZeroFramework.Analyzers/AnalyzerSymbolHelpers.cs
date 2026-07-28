using Microsoft.CodeAnalysis;

namespace SubZeroFramework.Analyzers;

internal static class AnalyzerSymbolHelpers
{
    internal const string ObservableObjectMetadataName = "CommunityToolkit.Mvvm.ComponentModel.ObservableObject";
    internal const string ObservablePropertyAttributeMetadataName = "CommunityToolkit.Mvvm.ComponentModel.ObservablePropertyAttribute";
    internal const string RelayCommandInterfaceMetadataName = "CommunityToolkit.Mvvm.Input.IRelayCommand";
    internal const string PropertyChangedEventHandlerMetadataName = "System.ComponentModel.PropertyChangedEventHandler";
    internal const string DictionaryMetadataName = "System.Collections.Generic.Dictionary`2";
    internal const string DictionaryInterfaceMetadataName = "System.Collections.Generic.IDictionary`2";
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

    /// <summary>
    /// True when <paramref name="type"/> IS the named type or implements it.
    /// </summary>
    /// <remarks>
    /// Needed because neither existing helper covers "the type is the interface itself" for a generic:
    /// <see cref="IsType"/> compares a display string ("System.IObservable&lt;T&gt;") against a metadata name
    /// ("System.IObservable`1"), which never matches for a generic type, and
    /// <see cref="ImplementsInterface"/> looks at AllInterfaces, which does not include the type itself.
    /// Together that meant a receiver STATICALLY typed as IObservable&lt;T&gt; — which is what every
    /// Watch*/Connect* method and every Rx operator returns — failed both checks.
    /// </remarks>
    internal static bool IsOrImplementsInterface(ITypeSymbol? type, Compilation compilation, string metadataName)
    {
        var expectedType = compilation.GetTypeByMetadataName(metadataName);
        if (expectedType is null || type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, expectedType)
            || namedType.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, expectedType));
    }

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

    internal static bool IsDisposableRegistry(ITypeSymbol? type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol namedType || namedType.TypeArguments.Length != 2)
        {
            return false;
        }

        var isDictionaryLike = IsType(namedType.OriginalDefinition, DictionaryMetadataName)
            || IsType(namedType.OriginalDefinition, DictionaryInterfaceMetadataName)
            || ImplementsInterface(namedType, compilation, DictionaryInterfaceMetadataName);

        if (!isDictionaryLike)
        {
            return false;
        }

        var valueType = namedType.TypeArguments[1];
        return IsType(valueType, IDisposableMetadataName)
            || ImplementsInterface(valueType as INamedTypeSymbol, compilation, IDisposableMetadataName);
    }
}