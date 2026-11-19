using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Read ECMA-335 metadata only. The inspected assembly is never loaded.
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
try
{
    if (!pe.HasMetadata) return 1;
    var metadata = pe.GetMetadataReader();
    if (!metadata.IsAssembly) return 1;
    var assembly = metadata.GetAssemblyDefinition();
    if (metadata.GetString(assembly.Name) != args[1] || assembly.Version.ToString(3) != args[2]) return 1;
    foreach (var handle in metadata.TypeDefinitions)
    {
        var type = metadata.GetTypeDefinition(handle);
        if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public ||
            type.Attributes.HasFlag(TypeAttributes.Abstract)) continue;
        var current = type;
        var ancestors = new HashSet<TypeDefinitionHandle>();
        while (true)
        {
            foreach (var iface in current.GetInterfaceImplementations())
            {
                var target = metadata.GetInterfaceImplementation(iface).Interface;
                if (target.Kind != HandleKind.TypeReference) continue;
                var reference = metadata.GetTypeReference((TypeReferenceHandle)target);
                if (metadata.GetString(reference.Name) != "IActPluginV1" ||
                    metadata.GetString(reference.Namespace) != "Advanced_Combat_Tracker" ||
                    reference.ResolutionScope.Kind != HandleKind.AssemblyReference) continue;
                var owner = metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope);
                if (metadata.GetString(owner.Name) == "Advanced Combat Tracker") return 0;
            }
            if (current.BaseType.Kind != HandleKind.TypeDefinition) break;
            var parent = (TypeDefinitionHandle)current.BaseType;
            if (!ancestors.Add(parent)) break;
            current = metadata.GetTypeDefinition(parent);
        }
    }
}
catch (BadImageFormatException) { }
return 1;
