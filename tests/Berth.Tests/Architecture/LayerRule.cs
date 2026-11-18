using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Berth.Tests.Architecture;

/// <summary>
/// The foundation plan's one architectural rule, read from compiled metadata without loading the assembly: a type
/// in the scanned namespace may reference only types of that namespace (or the compiler's own helpers) in its own
/// assembly, and types of the base .NET runtime (<c>Microsoft.NETCore.App</c>), and never a COM type. Everything a
/// type can reference is read: base type, interfaces, generic constraints, fields, properties, events, method
/// signatures, locals, custom attributes and every token in its method bodies, with generic arguments decoded.
/// </summary>
internal static class LayerRule
{
    private const string ComTypesNamespace = "System.Runtime.InteropServices.ComTypes";

    private static readonly IReadOnlyDictionary<short, OperandType> OperandTypes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value, code => code.OperandType);

    /// <summary>The assemblies of the base runtime: every file in the folder that holds <see cref="object"/>.</summary>
    private static readonly IReadOnlySet<string> BaseRuntime = Directory
        .EnumerateFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
        .Select(Path.GetFileNameWithoutExtension)
        .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

    /// <summary>Every violation in the types of <paramref name="namespacePrefix"/>, one line each.</summary>
    public static IReadOnlyList<string> Check(string assemblyPath, string namespacePrefix)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var scan = new Scan(pe, namespacePrefix);
        scan.Run();
        return scan.Violations.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The full names of the types <see cref="Check"/> scans, nested and compiler-generated ones included.</summary>
    public static IReadOnlyList<string> CoreTypes(string assemblyPath, string namespacePrefix)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var scan = new Scan(pe, namespacePrefix);
        return scan.Selected().Select(scan.FullName).ToList();
    }

    /// <summary>Refuses an empty selection, so a rule over no types can never pass.</summary>
    public static void RequireAny(IReadOnlyCollection<string> types, string namespacePrefix)
    {
        if (types.Count == 0)
        {
            throw new InvalidOperationException($"no type in {namespacePrefix}");
        }
    }

    /// <summary>A type a scanned type refers to: in this assembly (<see cref="Definition"/>) or another one.</summary>
    private readonly record struct Ref(string Assembly, string Namespace, string Name, TypeDefinitionHandle Definition)
    {
        public string FullName => Namespace.Length == 0 ? Name : Namespace + "." + Name;
    }

    private sealed class Scan(PEReader pe, string prefix) : ISignatureTypeProvider<ImmutableArray<Ref>, object?>
    {
        private readonly MetadataReader md = pe.GetMetadataReader();
        private readonly HashSet<string> violations = new(StringComparer.Ordinal);
        private readonly HashSet<TypeDefinitionHandle> scanned = [];

        public IEnumerable<string> Violations => violations;

        private string OwnAssembly => md.GetString(md.GetAssemblyDefinition().Name);

        public IEnumerable<TypeDefinitionHandle> Selected() =>
            md.TypeDefinitions.Where(handle => InPrefix(OuterNamespace(handle)));

        public void Run()
        {
            foreach (TypeDefinitionHandle handle in Selected())
            {
                ScanType(handle);
            }
        }

        public string FullName(TypeDefinitionHandle handle)
        {
            TypeDefinition type = md.GetTypeDefinition(handle);
            string name = md.GetString(type.Name);
            TypeDefinitionHandle outer = type.GetDeclaringType();
            if (!outer.IsNil)
            {
                return FullName(outer) + "+" + name;
            }

            string ns = md.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        private bool InPrefix(string ns) => ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal);

        private string OuterNamespace(TypeDefinitionHandle handle)
        {
            TypeDefinition type = md.GetTypeDefinition(handle);
            return type.GetDeclaringType().IsNil ? md.GetString(type.Namespace) : OuterNamespace(type.GetDeclaringType());
        }

        private void ScanType(TypeDefinitionHandle handle)
        {
            if (!scanned.Add(handle))
            {
                return;
            }

            TypeDefinition type = md.GetTypeDefinition(handle);
            string owner = FullName(handle);
            if (IsCom(handle))
            {
                violations.Add($"{owner} is a COM type");
            }

            Use(owner, type.BaseType);
            foreach (InterfaceImplementationHandle implementation in type.GetInterfaceImplementations())
            {
                Use(owner, md.GetInterfaceImplementation(implementation).Interface);
            }

            Generic(owner, type.GetGenericParameters());
            Attributes(owner, type.GetCustomAttributes());

            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = md.GetFieldDefinition(fieldHandle);
                string member = owner + "." + md.GetString(field.Name);
                Check(member, field.DecodeSignature(this, null));
                Attributes(member, field.GetCustomAttributes());
            }

            foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
            {
                PropertyDefinition property = md.GetPropertyDefinition(propertyHandle);
                string member = owner + "." + md.GetString(property.Name);
                MethodSignature<ImmutableArray<Ref>> signature = property.DecodeSignature(this, null);
                Check(member, signature.ReturnType);
                signature.ParameterTypes.ToList().ForEach(p => Check(member, p));
                Attributes(member, property.GetCustomAttributes());
            }

            foreach (EventDefinitionHandle eventHandle in type.GetEvents())
            {
                EventDefinition @event = md.GetEventDefinition(eventHandle);
                string member = owner + "." + md.GetString(@event.Name);
                Use(member, @event.Type);
                Attributes(member, @event.GetCustomAttributes());
            }

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                ScanMethod(owner, methodHandle);
            }
        }

        private void ScanMethod(string owner, MethodDefinitionHandle handle)
        {
            MethodDefinition method = md.GetMethodDefinition(handle);
            string member = owner + "." + md.GetString(method.Name);
            MethodSignature<ImmutableArray<Ref>> signature = method.DecodeSignature(this, null);
            Check(member, signature.ReturnType);
            signature.ParameterTypes.ToList().ForEach(p => Check(member, p));
            Generic(member, method.GetGenericParameters());
            Attributes(member, method.GetCustomAttributes());
            foreach (ParameterHandle parameter in method.GetParameters())
            {
                Attributes(member, md.GetParameter(parameter).GetCustomAttributes());
            }

            if (method.RelativeVirtualAddress == 0)
            {
                return;
            }

            MethodBodyBlock body = pe.GetMethodBody(method.RelativeVirtualAddress);
            if (!body.LocalSignature.IsNil)
            {
                StandaloneSignature locals = md.GetStandaloneSignature(body.LocalSignature);
                locals.DecodeLocalSignature(this, null).ToList().ForEach(l => Check(member, l));
            }

            foreach (EntityHandle token in Tokens(body.GetILBytes()))
            {
                Use(member, token);
            }

            // A catch clause names its exception type in the region table, not in the IL.
            foreach (ExceptionRegion region in body.ExceptionRegions)
            {
                Use(member, region.CatchType);
            }
        }

        private static IEnumerable<EntityHandle> Tokens(byte[]? il)
        {
            if (il is null)
            {
                yield break;
            }

            int at = 0;
            while (at < il.Length)
            {
                short value = il[at] == 0xFE ? (short)(0xFE00 | il[at + 1]) : il[at];
                at += il[at] == 0xFE ? 2 : 1;
                OperandType operand = OperandTypes[value];
                switch (operand)
                {
                    case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineSig
                        or OperandType.InlineTok or OperandType.InlineType:
                        yield return MetadataTokens.EntityHandle(BitConverter.ToInt32(il, at));
                        at += 4;
                        break;
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                        at += 1;
                        break;
                    case OperandType.InlineVar:
                        at += 2;
                        break;
                    case OperandType.InlineI8 or OperandType.InlineR:
                        at += 8;
                        break;
                    case OperandType.InlineSwitch:
                        at += 4 + (4 * BitConverter.ToInt32(il, at));
                        break;
                    default:
                        at += 4;
                        break;
                }
            }
        }

        private void Generic(string member, GenericParameterHandleCollection parameters)
        {
            foreach (GenericParameterHandle parameterHandle in parameters)
            {
                GenericParameter parameter = md.GetGenericParameter(parameterHandle);
                foreach (GenericParameterConstraintHandle constraint in parameter.GetConstraints())
                {
                    Use(member, md.GetGenericParameterConstraint(constraint).Type);
                }

                Attributes(member, parameter.GetCustomAttributes());
            }
        }

        private void Attributes(string member, CustomAttributeHandleCollection attributes)
        {
            foreach (CustomAttributeHandle handle in attributes)
            {
                CustomAttribute attribute = md.GetCustomAttribute(handle);
                Use(member, attribute.Constructor);

                // typeof(...) values are serialized into the attribute blob, outside every signature.
                CustomAttributeValue<ImmutableArray<Ref>> value;
                try
                {
                    value = attribute.DecodeValue(new AttributeTypes(this));
                }
                catch (BadImageFormatException error)
                {
                    // Fail closed: an attribute the rule cannot read is reported, never passed.
                    violations.Add($"{member} has an attribute whose value cannot be read: {error.Message}");
                    continue;
                }
                foreach (CustomAttributeTypedArgument<ImmutableArray<Ref>> argument in value.FixedArguments)
                {
                    Check(member, ArgumentTypes(argument));
                }

                foreach (CustomAttributeNamedArgument<ImmutableArray<Ref>> argument in value.NamedArguments)
                {
                    Check(member, argument.Type.AddRange(ArgumentTypes(new(argument.Type, argument.Value))));
                }
            }
        }

        private static ImmutableArray<Ref> ArgumentTypes(CustomAttributeTypedArgument<ImmutableArray<Ref>> argument) =>
            argument.Value switch
            {
                ImmutableArray<Ref> type => type,
                ImmutableArray<CustomAttributeTypedArgument<ImmutableArray<Ref>>> items => items.SelectMany(item => ArgumentTypes(item)).ToImmutableArray(),
                _ => [],
            };

        /// <summary>
        /// Every type an attribute blob names by its serialized name, parsed by .NET's own TypeName parser so the
        /// generic arguments, array elements and assembly of each part all count.
        /// </summary>
        internal ImmutableArray<Ref> FromSerializedName(string serialized) => FromTypeName(TypeName.Parse(serialized), null);

        private ImmutableArray<Ref> FromTypeName(TypeName type, string? assembly)
        {
            string? owner = type.AssemblyName?.Name ?? assembly;
            if (type.IsArray || type.IsPointer || type.IsByRef)
            {
                return FromTypeName(type.GetElementType(), owner);
            }

            if (type.IsConstructedGenericType)
            {
                ImmutableArray<Ref> found = FromTypeName(type.GetGenericTypeDefinition(), owner);
                foreach (TypeName argument in type.GetGenericArguments())
                {
                    found = found.AddRange(FromTypeName(argument, null));
                }

                return found;
            }

            string full = type.FullName;
            if (owner is null || owner.Equals(OwnAssembly, StringComparison.OrdinalIgnoreCase))
            {
                // Unqualified or this assembly: this assembly when it defines the type, else the core library.
                TypeDefinitionHandle own = md.TypeDefinitions.FirstOrDefault(d => FullName(d) == full);
                if (!own.IsNil)
                {
                    return [Definition(own)];
                }

                owner = owner is null ? "System.Private.CoreLib" : OwnAssembly;
            }

            string outer = full.Split('+')[0];
            int dot = outer.LastIndexOf('.');
            string ns = dot < 0 ? string.Empty : outer[..dot];
            return [new Ref(owner, ns, dot < 0 ? full : full[(dot + 1)..], default)];
        }
        /// <summary>An enum's underlying type: read from its value__ field when this assembly defines it.</summary>
        internal PrimitiveTypeCode UnderlyingEnumType(ImmutableArray<Ref> type)
        {
            if (type.Length == 1 && !type[0].Definition.IsNil)
            {
                foreach (FieldDefinitionHandle handle in md.GetTypeDefinition(type[0].Definition).GetFields())
                {
                    FieldDefinition field = md.GetFieldDefinition(handle);
                    if (md.GetString(field.Name) == "value__")
                    {
                        BlobReader blob = md.GetBlobReader(field.Signature);
                        blob.ReadSignatureHeader();
                        return (PrimitiveTypeCode)blob.ReadCompressedInteger();
                    }
                }
            }

            // A base-runtime enum in an attribute (AttributeTargets and the like) is an int; anything else fails
            // to decode and is reported by the caller.
            return PrimitiveTypeCode.Int32;
        }

        private void Use(string member, EntityHandle handle) => Check(member, Resolve(handle));

        private ImmutableArray<Ref> Resolve(EntityHandle handle)
        {
            if (handle.IsNil)
            {
                return [];
            }

            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    return [Definition((TypeDefinitionHandle)handle)];
                case HandleKind.TypeReference:
                    return [Reference((TypeReferenceHandle)handle)];
                case HandleKind.TypeSpecification:
                    return md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null);
                case HandleKind.MethodDefinition:
                    return [Definition(md.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType())];
                case HandleKind.FieldDefinition:
                    return [Definition(md.GetFieldDefinition((FieldDefinitionHandle)handle).GetDeclaringType())];
                case HandleKind.MemberReference:
                    MemberReference reference = md.GetMemberReference((MemberReferenceHandle)handle);
                    ImmutableArray<Ref> parent = Resolve(reference.Parent);
                    if (reference.GetKind() == MemberReferenceKind.Field)
                    {
                        return parent.AddRange(reference.DecodeFieldSignature(this, null));
                    }

                    MethodSignature<ImmutableArray<Ref>> called = reference.DecodeMethodSignature(this, null);
                    return parent.AddRange(called.ReturnType).AddRange(called.ParameterTypes.SelectMany(p => p));
                case HandleKind.MethodSpecification:
                    MethodSpecification specification = md.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return Resolve(specification.Method)
                        .AddRange(specification.DecodeSignature(this, null).SelectMany(a => a));
                case HandleKind.StandaloneSignature:
                    StandaloneSignature standalone = md.GetStandaloneSignature((StandaloneSignatureHandle)handle);
                    if (standalone.GetKind() == StandaloneSignatureKind.LocalVariables)
                    {
                        return standalone.DecodeLocalSignature(this, null).SelectMany(l => l).ToImmutableArray();
                    }

                    MethodSignature<ImmutableArray<Ref>> pointer = standalone.DecodeMethodSignature(this, null);
                    return pointer.ReturnType.AddRange(pointer.ParameterTypes.SelectMany(p => p));
                default:
                    return [];
            }
        }

        private void Check(string member, ImmutableArray<Ref> refs)
        {
            foreach (Ref used in refs)
            {
                if (used.Definition.IsNil)
                {
                    if (!BaseRuntime.Contains(used.Assembly))
                    {
                        violations.Add($"{member} uses {used.FullName} from {used.Assembly}");
                    }
                    else if (used.Namespace == ComTypesNamespace || used.Namespace.StartsWith(ComTypesNamespace + ".", StringComparison.Ordinal))
                    {
                        violations.Add($"{member} uses {used.FullName} from {used.Assembly}");
                    }

                    continue;
                }

                if (IsCom(used.Definition))
                {
                    violations.Add($"{member} uses {used.FullName} from {used.Assembly}");
                }
                else if (!InPrefix(OuterNamespace(used.Definition)))
                {
                    if (IsCompilerHelper(used.Definition))
                    {
                        // A helper the compiler or a generator added for this type is held to the same rule.
                        ScanType(used.Definition);
                    }
                    else
                    {
                        violations.Add($"{member} uses {used.FullName} from {used.Assembly}");
                    }
                }
            }
        }

        private Ref Definition(TypeDefinitionHandle handle)
        {
            string full = FullName(handle);
            string ns = OuterNamespace(handle);
            string name = ns.Length == 0 ? full : full[(ns.Length + 1)..];
            return new Ref(OwnAssembly, ns, name, handle);
        }

        private Ref Reference(TypeReferenceHandle handle)
        {
            TypeReference type = md.GetTypeReference(handle);
            string name = md.GetString(type.Name);
            switch (type.ResolutionScope.Kind)
            {
                case HandleKind.TypeReference:
                    Ref outer = Reference((TypeReferenceHandle)type.ResolutionScope);
                    return outer with { Name = outer.Name + "+" + name };
                case HandleKind.AssemblyReference:
                    string assembly = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name);
                    return new Ref(assembly, md.GetString(type.Namespace), name, default);
                default:
                    // A reference to a type of this module: find its definition so its namespace and COM flag count.
                    string ns = md.GetString(type.Namespace);
                    TypeDefinitionHandle match = md.TypeDefinitions.FirstOrDefault(d =>
                        md.GetString(md.GetTypeDefinition(d).Name) == name && md.GetString(md.GetTypeDefinition(d).Namespace) == ns);
                    return new Ref(OwnAssembly, ns, name, match);
            }
        }

        private bool IsCom(TypeDefinitionHandle handle)
        {
            TypeDefinition type = md.GetTypeDefinition(handle);
            return (type.Attributes & TypeAttributes.Import) != 0
                || HasAttribute(type.GetCustomAttributes(), "System.Runtime.InteropServices.Marshalling", "GeneratedComInterfaceAttribute");
        }

        /// <summary>
        /// A type the compiler itself adds to the assembly, and nothing else: a top-level type whose name C# cannot
        /// spell (<c>&lt;PrivateImplementationDetails&gt;</c>), or an attribute the compiler embeds (nullable,
        /// ref-safety and the like) in the namespaces it uses for them. A source type marked [CompilerGenerated] is
        /// not exempt.
        /// </summary>
        private bool IsCompilerHelper(TypeDefinitionHandle handle)
        {
            TypeDefinitionHandle outermost = handle;
            while (!md.GetTypeDefinition(outermost).GetDeclaringType().IsNil)
            {
                outermost = md.GetTypeDefinition(outermost).GetDeclaringType();
            }

            TypeDefinition type = md.GetTypeDefinition(outermost);
            string ns = md.GetString(type.Namespace);

            // C# cannot spell a name starting '<': only the compiler or a source generator makes one
            // (<PrivateImplementationDetails>, file-local generated helpers such as the regex generator's, and the
            // types nested in them).
            if (md.GetString(type.Name).StartsWith('<'))
            {
                return true;
            }

            return ns is "System.Runtime.CompilerServices" or "Microsoft.CodeAnalysis" or "System.Diagnostics.CodeAnalysis"
                && HasAttribute(type.GetCustomAttributes(), "Microsoft.CodeAnalysis", "EmbeddedAttribute");
        }

        private bool HasAttribute(CustomAttributeHandleCollection attributes, string ns, string name)
        {
            foreach (CustomAttributeHandle handle in attributes)
            {
                EntityHandle constructor = md.GetCustomAttribute(handle).Constructor;
                EntityHandle owner = constructor.Kind switch
                {
                    HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)constructor).Parent,
                    HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
                    _ => default,
                };
                (string Namespace, string Name)? found = owner.Kind switch
                {
                    HandleKind.TypeReference => (md.GetString(md.GetTypeReference((TypeReferenceHandle)owner).Namespace), md.GetString(md.GetTypeReference((TypeReferenceHandle)owner).Name)),
                    HandleKind.TypeDefinition => (md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)owner).Namespace), md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)owner).Name)),
                    _ => null,
                };
                if (found is { } f && f.Namespace == ns && f.Name == name)
                {
                    return true;
                }
            }

            return false;
        }

        // ISignatureTypeProvider: every decoded type becomes the set of types it names.
        public ImmutableArray<Ref> GetPrimitiveType(PrimitiveTypeCode typeCode) => [];

        public ImmutableArray<Ref> GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => [Definition(handle)];

        public ImmutableArray<Ref> GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => [Reference(handle)];

        public ImmutableArray<Ref> GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public ImmutableArray<Ref> GetSZArrayType(ImmutableArray<Ref> elementType) => elementType;

        public ImmutableArray<Ref> GetArrayType(ImmutableArray<Ref> elementType, ArrayShape shape) => elementType;

        public ImmutableArray<Ref> GetByReferenceType(ImmutableArray<Ref> elementType) => elementType;

        public ImmutableArray<Ref> GetPointerType(ImmutableArray<Ref> elementType) => elementType;

        public ImmutableArray<Ref> GetPinnedType(ImmutableArray<Ref> elementType) => elementType;

        public ImmutableArray<Ref> GetModifiedType(ImmutableArray<Ref> modifier, ImmutableArray<Ref> unmodifiedType, bool isRequired) =>
            modifier.AddRange(unmodifiedType);

        public ImmutableArray<Ref> GetGenericInstantiation(ImmutableArray<Ref> genericType, ImmutableArray<ImmutableArray<Ref>> typeArguments) =>
            genericType.AddRange(typeArguments.SelectMany(a => a));

        public ImmutableArray<Ref> GetGenericMethodParameter(object? genericContext, int index) => [];

        public ImmutableArray<Ref> GetGenericTypeParameter(object? genericContext, int index) => [];

        public ImmutableArray<Ref> GetFunctionPointerType(MethodSignature<ImmutableArray<Ref>> signature) =>
            signature.ReturnType.AddRange(signature.ParameterTypes.SelectMany(p => p));
    }

    /// <summary>Decodes attribute blobs into the types they name.</summary>
    private sealed class AttributeTypes(Scan scan) : ICustomAttributeTypeProvider<ImmutableArray<Ref>>
    {
        private static readonly ImmutableArray<Ref> SystemType = [new Ref("System.Private.CoreLib", "System", "Type", default)];

        public ImmutableArray<Ref> GetPrimitiveType(PrimitiveTypeCode typeCode) => [];

        public ImmutableArray<Ref> GetSystemType() => SystemType;

        public ImmutableArray<Ref> GetSZArrayType(ImmutableArray<Ref> elementType) => elementType;

        public ImmutableArray<Ref> GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            scan.GetTypeFromDefinition(reader, handle, rawTypeKind);

        public ImmutableArray<Ref> GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            scan.GetTypeFromReference(reader, handle, rawTypeKind);

        public ImmutableArray<Ref> GetTypeFromSerializedName(string name) => scan.FromSerializedName(name);

        public PrimitiveTypeCode GetUnderlyingEnumType(ImmutableArray<Ref> type) => scan.UnderlyingEnumType(type);

        public bool IsSystemType(ImmutableArray<Ref> type) => type.Length == 1 && type[0].FullName == "System.Type";
    }
}
