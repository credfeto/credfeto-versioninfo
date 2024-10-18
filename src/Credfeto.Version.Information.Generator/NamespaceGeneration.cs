using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Credfeto.Version.Information.Generator;

[DebuggerDisplay("{Namespace}")]
public readonly record struct NamespaceGeneration
{
    public NamespaceGeneration(AssemblyIdentity assembly, ImmutableDictionary<string, string> attributes)
    {
        this.Assembly = assembly;
        this.Attributes = attributes;
    }

    public string Namespace => this.Assembly.Name;

    public AssemblyIdentity Assembly { get; }

    public ImmutableDictionary<string, string> Attributes { get; }
}