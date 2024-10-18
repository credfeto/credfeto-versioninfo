using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Credfeto.Version.Information.Generator.Builders;
using Credfeto.Version.Information.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Credfeto.Version.Information.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class VersionInformationCodeGenerator : IIncrementalGenerator
{
    private const string CLASS_NAME = "VersionInformation";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<(NamespaceGeneration? classInfo, ErrorInfo? errorInfo)> namespaces =
            context.SyntaxProvider.CreateSyntaxProvider(predicate: static (n, _) => n is NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax, transform: GetNamespace);

        IncrementalValuesProvider<((NamespaceGeneration? classInfo, ErrorInfo? errorInfo) Left, AnalyzerConfigOptionsProvider Right)> withOptions =
            namespaces.Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(source: withOptions, action: GenerateVersionInformation);
    }

    private static (NamespaceGeneration? namespaceInfo, ErrorInfo? errorInfo) GetNamespace(GeneratorSyntaxContext generatorSyntaxContext, CancellationToken cancellationToken)
    {
        if (generatorSyntaxContext.Node is not NamespaceDeclarationSyntax and not FileScopedNamespaceDeclarationSyntax)
        {
            return (null, InvalidInfo(generatorSyntaxContext));
        }

        Compilation compilation = generatorSyntaxContext.SemanticModel.Compilation;

        AssemblyIdentity assembly = GetAssembly(compilation);

        ImmutableDictionary<string, string> attributes = ExtractAttributes(compilation.Assembly);

        return (new NamespaceGeneration(assembly: assembly, attributes: attributes), null);
    }

    private static ErrorInfo InvalidInfo(in GeneratorSyntaxContext generatorSyntaxContext)
    {
        return new ErrorInfo(generatorSyntaxContext.Node.GetLocation(), new InvalidOperationException("Expected a namespace declaration"));
    }

    private static AssemblyIdentity GetAssembly(Compilation compilation)
    {
        return compilation.Assembly.Identity;
    }

    private static void ReportException(Location location, in SourceProductionContext context, Exception exception)
    {
        context.ReportDiagnostic(diagnostic: Diagnostic.Create(new(id: "VER002",
                                                                   title: "Unhandled Exception",
                                                                   exception.Message + ' ' + exception.StackTrace,
                                                                   category: RuntimeVersionInformation.ToolName,
                                                                   defaultSeverity: DiagnosticSeverity.Error,
                                                                   isEnabledByDefault: true),
                                                               location: location));
    }

    private static void GenerateVersionInformation(SourceProductionContext sourceProductionContext,
                                                   ((NamespaceGeneration? namespaceInfo, ErrorInfo? errorInfo) Left, AnalyzerConfigOptionsProvider Right) item)
    {
        if (item.Left.errorInfo is not null)
        {
            ErrorInfo ei = item.Left.errorInfo.Value;
            ReportException(location: ei.Location, context: sourceProductionContext, exception: ei.Exception);

            return;
        }

        if (item.Left.namespaceInfo is null)
        {
            return;
        }

        GenerateVersionInformation(sourceProductionContext: sourceProductionContext, namespaceInfo: item.Left.namespaceInfo.Value, analyzerConfigOptionsProvider: item.Right);
    }

    private static void GenerateVersionInformation(in SourceProductionContext sourceProductionContext,
                                                   in NamespaceGeneration namespaceInfo,
                                                   AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider)
    {
        ImmutableDictionary<string, string> attributes = namespaceInfo.Attributes;

        string assemblyNamespace = namespaceInfo.Namespace;
        string product = GetAssemblyProduct(analyzerConfigOptionsProvider: analyzerConfigOptionsProvider, attributes: attributes, assemblyNamespace: assemblyNamespace);
        string version = CleanVersion(GetAssemblyVersion(assemblyIdentity: namespaceInfo.Assembly, attributes: attributes));

        CodeBuilder source = BuildSource(assemblyNamespace: assemblyNamespace, version: version, product: product, attributes: attributes);

        sourceProductionContext.AddSource($"{assemblyNamespace}.{CLASS_NAME}.generated.cs", sourceText: source.Text);
    }

    private static CodeBuilder BuildSource(string assemblyNamespace, string version, string product, in ImmutableDictionary<string, string> attributes)
    {
        CodeBuilder source = new();

        source.AppendFileHeader()
              .AppendLine("using System;")
              .AppendLine("using System.CodeDom.Compiler;")
              .AppendBlankLine()
              .AppendLine($"namespace {assemblyNamespace};")
              .AppendBlankLine()
              .AppendGeneratedCodeAttribute();

        using (source.StartBlock("internal static class VersionInformation"))
        {
            DumpAttributes(attributes: attributes, source: source);

            source.AppendPublicConstant(key: "Version", value: version)
                  .AppendPublicConstant(key: "Product", value: product)
                  .AppendAttributeValue(attributes: attributes, nameof(AssemblyCompanyAttribute), key: "Company")
                  .AppendAttributeValue(attributes: attributes, nameof(AssemblyCopyrightAttribute), key: "Copyright");
        }

        return source;
    }

    [Conditional("DEBUG")]
    private static void DumpAttributes(ImmutableDictionary<string, string> attributes, CodeBuilder source)
    {
        foreach (string key in attributes.Keys)
        {
            if (!attributes.TryGetValue(key: key, out string? value))
            {
                continue;
            }

            source.AppendLine($"// {key} = {value}");
        }
    }

    private static ImmutableDictionary<string, string> ExtractAttributes(in IAssemblySymbol ass)
    {
        ImmutableArray<AttributeData> attibuteData = ass.GetAttributes();

        ImmutableDictionary<string, string> attributes = ImmutableDictionary<string, string>.Empty;

        foreach (AttributeData a in attibuteData)
        {
            if (a.AttributeClass is null || a.ConstructorArguments.Length != 1)
            {
                continue;
            }

            string key = a.AttributeClass.Name;

            if (attributes.ContainsKey(key))
            {
                continue;
            }

            object? v = a.ConstructorArguments[0].Value;
            string value = v?.ToString() ?? string.Empty;

            attributes = attributes.Add(key: key, value: value);
        }

        return attributes;
    }

    private static string GetAssemblyProduct(in AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider, ImmutableDictionary<string, string> attributes, string assemblyNamespace)
    {
        if (analyzerConfigOptionsProvider.GlobalOptions.TryGetValue(key: "build_property.rootnamespace", out string? product) && !string.IsNullOrWhiteSpace(product))
        {
            return product;
        }

        if (attributes.TryGetValue(nameof(AssemblyTitleAttribute), value: out product) && !string.IsNullOrWhiteSpace(product))
        {
            return product;
        }

        return assemblyNamespace;
    }

    private static string GetAssemblyVersion(in AssemblyIdentity assemblyIdentity, ImmutableDictionary<string, string> attributes)
    {
        return attributes.TryGetValue(nameof(AssemblyInformationalVersionAttribute), out string? version) && !string.IsNullOrWhiteSpace(version)
            ? version
            : assemblyIdentity.Version.ToString();
    }

    private static string CleanVersion(string source)
    {
        int pos = source.IndexOf(value: '+');

        return pos != -1
            ? source.Substring(startIndex: 0, length: pos)
            : source;
    }
}