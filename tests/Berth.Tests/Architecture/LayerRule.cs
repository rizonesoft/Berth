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
            foreach (CustomAttributeHandle attribute in attributes)
            {
                Use(member, md.GetCustomAttribute(attribute).Constructor);
            }
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
                        violations.Add($"{member} uses the COM type {used.FullName} from {used.Assembly}");
                    }

                    continue;
                }

                if (IsCom(used.Definition))
                {
                    violations.Add($"{member} uses the COM type {used.FullName} from {used.Assembly}");
                }
                else if (!InPrefix(OuterNamespace(used.Definition)) && !IsCompilerHelper(used.Definition))
                {
                    violations.Add($"{member} uses {used.FullName} from {used.Assembly}");
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

        /// <summary>A type the compiler adds to the assembly itself (nullable attributes, private details).</summary>
        private bool IsCompilerHelper(TypeDefinitionHandle handle)
        {
            TypeDefinition type = md.GetTypeDefinition(handle);
            return md.GetString(type.Name).StartsWith('<')
                || HasAttribute(type.GetCustomAttributes(), "System.Runtime.CompilerServices", "CompilerGeneratedAttribute")
                || HasAttribute(type.GetCustomAttributes(), "Microsoft.CodeAnalysis", "EmbeddedAttribute");
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
}
