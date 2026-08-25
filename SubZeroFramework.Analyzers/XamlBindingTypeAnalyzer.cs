using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SubZeroFramework.Analyzers;

/// <summary>
/// Catches a string bound to an enum-typed XAML property, which compiles and then throws on page load.
/// </summary>
/// <remarks>
/// <para>
/// The generated <c>x:Bind</c> setter routes a mismatched type through
/// <c>XamlBindingHelper.ConvertValue</c>, which has no string-to-enum conversion and raises
/// <c>ArgumentException: The value cannot be converted to type ...</c>. Nothing fails at build time, so the
/// mistake reaches a running app and presents as a crash on navigation rather than as a binding problem —
/// which is why it recurred across five view models in this repo before it was caught.
/// </para>
/// <para>
/// <b>Deliberately narrow.</b> It reports only "target is an enum, source is a string", not type mismatches
/// in general. XAML legitimately converts plenty of things — bool to Visibility, int to double — and flagging
/// those would bury the one combination that actually throws.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class XamlBindingTypeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Namespaces the default XAML namespace is searched for, in order.
    /// </summary>
    /// <remarks>
    /// The default namespace maps to no single CLR namespace, so an unprefixed element has to be probed for.
    /// Failing to resolve one is harmless — the binding is then skipped rather than guessed at.
    /// </remarks>
    private static readonly string[] DefaultXamlNamespaces =
    [
        "Microsoft.UI.Xaml.Controls",
        "Microsoft.UI.Xaml.Controls.Primitives",
        "Microsoft.UI.Xaml",
        "Microsoft.UI.Xaml.Shapes",
        "Microsoft.UI.Xaml.Documents",
    ];

    private const string UsingScheme = "using:";
    private const string ClrNamespaceScheme = "clr-namespace:";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        SubZeroDiagnosticDescriptors.XamlBindingMustNotConvertStringToEnum,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Once per compilation rather than per syntax tree: the bindings live in XAML, which reaches an
        // analyzer only as an additional file and has no relationship to any particular C# file.
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        foreach (var file in context.Options.AdditionalFiles)
        {
            if (!file.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeXamlFile(context, file);
        }
    }

    private static void AnalyzeXamlFile(CompilationAnalysisContext context, AdditionalText file)
    {
        var text = file.GetText(context.CancellationToken);
        if (text is null)
        {
            return;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(text.ToString(), LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            // Malformed XAML is the XAML compiler's complaint to make, not this one's.
            return;
        }

        var root = document.Root;
        if (root is null)
        {
            return;
        }

        // What an unqualified x:Bind path resolves against for this file, unless a DataTemplate overrides it.
        var rootType = ResolveRootType(context.Compilation, root);

        foreach (var element in root.DescendantsAndSelf())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var elementType = ResolveElementType(context.Compilation, element);
            if (elementType is null)
            {
                continue;
            }

            // A DataTemplate's x:DataType replaces the page as the binding source for everything inside it.
            var sourceType = ResolveDataType(context.Compilation, element) ?? rootType;
            if (sourceType is null)
            {
                continue;
            }

            foreach (var attribute in element.Attributes())
            {
                AnalyzeAttribute(context, file, text, attribute, elementType, sourceType);
            }
        }
    }

    private static void AnalyzeAttribute(
        CompilationAnalysisContext context,
        AdditionalText file,
        SourceText text,
        XAttribute attribute,
        INamedTypeSymbol elementType,
        INamedTypeSymbol sourceType)
    {
        if (attribute.IsNamespaceDeclaration || attribute.Name.LocalName.IndexOf('.') >= 0)
        {
            // Attached properties (Grid.Row, ToolTipService.ToolTip) are declared on a different type than
            // the element they sit on, so resolving them against the element would be wrong rather than
            // merely incomplete.
            return;
        }

        var path = ExtractBindingPath(attribute.Value);
        if (path is null)
        {
            return;
        }

        // The target has to be an enum for this to be the failure in question.
        var targetProperty = FindMember(elementType, attribute.Name.LocalName);
        if (targetProperty is null || Unwrap(targetProperty) is not { TypeKind: TypeKind.Enum } targetEnum)
        {
            return;
        }

        var resolved = ResolvePath(sourceType, path);
        if (resolved is null || resolved.SpecialType != SpecialType.System_String)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            SubZeroDiagnosticDescriptors.XamlBindingMustNotConvertStringToEnum,
            CreateLocation(file, text, attribute),
            path,
            elementType.Name,
            attribute.Name.LocalName,
            targetEnum.Name));
    }

    /// <summary>
    /// The bound path, or null when the attribute is not a binding this rule can judge.
    /// </summary>
    /// <remarks>
    /// Everything uncertain is skipped rather than guessed at. A binding that names a Converter is exempt
    /// outright — converting is then the converter's job, and this repo legitimately binds a string brush key
    /// to a Brush that way. Function bindings and empty paths are skipped because there is no property whose
    /// type could be checked.
    /// </remarks>
    private static string? ExtractBindingPath(string value)
    {
        var trimmed = value.Trim();

        var isBind = trimmed.StartsWith("{x:Bind", StringComparison.Ordinal);
        if (!isBind && !trimmed.StartsWith("{Binding", StringComparison.Ordinal))
        {
            return null;
        }

        if (!trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return null;
        }

        var body = trimmed.Substring(1, trimmed.Length - 2);
        var markup = isBind ? "x:Bind" : "Binding";
        body = body.Substring(markup.Length).Trim();

        var arguments = SplitTopLevel(body);
        string? path = null;

        foreach (var argument in arguments)
        {
            var separator = argument.IndexOf('=');

            if (separator < 0)
            {
                // Positional: the path itself, and only the first one is.
                path ??= argument.Trim();
                continue;
            }

            var name = argument.Substring(0, separator).Trim();

            if (string.Equals(name, "Converter", StringComparison.Ordinal))
            {
                return null;
            }

            if (string.Equals(name, "Path", StringComparison.Ordinal))
            {
                path = argument.Substring(separator + 1).Trim();
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        // Function bindings, indexers and casts have no single property to resolve.
        return path!.IndexOfAny(['(', ')', '[', ']']) >= 0 ? null : path;
    }

    /// <summary>
    /// Splits markup-extension arguments on commas, ignoring commas nested inside another extension.
    /// </summary>
    /// <remarks>
    /// A naive split would cut <c>Converter={StaticResource X}, ConverterParameter=A,B</c> into pieces that
    /// no longer parse, and the Converter exemption above depends on seeing that argument intact.
    /// </remarks>
    private static List<string> SplitTopLevel(string body)
    {
        List<string> parts = [];
        var depth = 0;
        var start = 0;

        for (var i = 0; i < body.Length; i++)
        {
            switch (body[i])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(body.Substring(start, i - start));
                    start = i + 1;
                    break;
            }
        }

        if (start < body.Length)
        {
            parts.Add(body.Substring(start));
        }

        return parts;
    }

    /// <summary>Walks a dotted path from a starting type, returning the final member's type.</summary>
    private static ITypeSymbol? ResolvePath(INamedTypeSymbol sourceType, string path)
    {
        ITypeSymbol? current = sourceType;

        foreach (var segment in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            var member = FindMember(current, segment.Trim());
            if (member is null)
            {
                return null;
            }

            current = Unwrap(member);
        }

        return current;
    }

    private static ITypeSymbol? FindMember(ITypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                switch (member)
                {
                    case IPropertySymbol property:
                        return property.Type;
                    case IFieldSymbol field:
                        return field.Type;
                }
            }
        }

        return null;
    }

    /// <summary>Nullable value types bind the same as their underlying type, so they resolve to it.</summary>
    private static ITypeSymbol? Unwrap(ITypeSymbol? type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    private static INamedTypeSymbol? ResolveRootType(Compilation compilation, XElement root)
    {
        var xClass = root.Attributes().FirstOrDefault(static attribute =>
            string.Equals(attribute.Name.LocalName, "Class", StringComparison.Ordinal)
            && attribute.Name.NamespaceName.EndsWith("/xaml", StringComparison.Ordinal));

        return xClass is null ? null : compilation.GetTypeByMetadataName(xClass.Value);
    }

    private static INamedTypeSymbol? ResolveDataType(Compilation compilation, XElement element)
    {
        // Nearest enclosing x:DataType wins, which is how a nested template's bindings actually resolve.
        for (var current = element; current is not null; current = current.Parent)
        {
            var dataType = current.Attributes().FirstOrDefault(static attribute =>
                string.Equals(attribute.Name.LocalName, "DataType", StringComparison.Ordinal)
                && attribute.Name.NamespaceName.EndsWith("/xaml", StringComparison.Ordinal));

            if (dataType is not null)
            {
                return ResolveQualifiedType(compilation, current, dataType.Value);
            }
        }

        return null;
    }

    /// <summary>Resolves a <c>prefix:TypeName</c> reference using the prefix declared on the document.</summary>
    private static INamedTypeSymbol? ResolveQualifiedType(Compilation compilation, XElement scope, string qualified)
    {
        var separator = qualified.IndexOf(':');
        var prefix = separator < 0 ? string.Empty : qualified.Substring(0, separator);
        var name = separator < 0 ? qualified : qualified.Substring(separator + 1);

        var xmlNamespace = scope.GetNamespaceOfPrefix(prefix)?.NamespaceName;
        if (xmlNamespace is null)
        {
            return null;
        }

        return ResolveTypeInXmlNamespace(compilation, xmlNamespace, name.Trim());
    }

    private static INamedTypeSymbol? ResolveElementType(Compilation compilation, XElement element)
    {
        var name = element.Name.LocalName;

        // Property-element syntax (<Grid.RowDefinitions>) names a property, not a type.
        return name.IndexOf('.') >= 0
            ? null
            : ResolveTypeInXmlNamespace(compilation, element.Name.NamespaceName, name);
    }

    private static INamedTypeSymbol? ResolveTypeInXmlNamespace(Compilation compilation, string xmlNamespace, string typeName)
    {
        if (xmlNamespace.StartsWith(UsingScheme, StringComparison.Ordinal))
        {
            return compilation.GetTypeByMetadataName($"{xmlNamespace.Substring(UsingScheme.Length)}.{typeName}");
        }

        if (xmlNamespace.StartsWith(ClrNamespaceScheme, StringComparison.Ordinal))
        {
            var clr = xmlNamespace.Substring(ClrNamespaceScheme.Length);
            var assembly = clr.IndexOf(';');
            if (assembly >= 0)
            {
                clr = clr.Substring(0, assembly);
            }

            return compilation.GetTypeByMetadataName($"{clr}.{typeName}");
        }

        // The default XAML namespace, which maps to no single CLR namespace.
        foreach (var candidate in DefaultXamlNamespaces)
        {
            if (compilation.GetTypeByMetadataName($"{candidate}.{typeName}") is { } resolved)
            {
                return resolved;
            }
        }

        return null;
    }

    /// <summary>
    /// Points the diagnostic at the offending attribute in the XAML itself.
    /// </summary>
    /// <remarks>
    /// The XAML is where the mistake is visible as a pair — the declaration alone looks perfectly reasonable
    /// until you know what it is bound to. Reporting on the C# property would name a type that is only wrong
    /// in the context of a file the message could not show.
    /// </remarks>
    private static Location CreateLocation(AdditionalText file, SourceText text, XAttribute attribute)
    {
        if (attribute is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return Location.Create(file.Path, default, default);
        }

        var lineIndex = lineInfo.LineNumber - 1;
        if (lineIndex < 0 || lineIndex >= text.Lines.Count)
        {
            return Location.Create(file.Path, default, default);
        }

        var line = text.Lines[lineIndex];
        var start = Math.Min(line.Start + Math.Max(0, lineInfo.LinePosition - 1), line.End);
        var length = Math.Min(attribute.Name.LocalName.Length, line.End - start);
        var span = new TextSpan(start, Math.Max(0, length));

        return Location.Create(file.Path, span, text.Lines.GetLinePositionSpan(span));
    }
}
