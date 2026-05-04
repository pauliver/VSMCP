using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- Code generation: implement interface, override, ctor, equality --------

    public async Task<AddMemberResult> CodeImplementInterfaceAsync(
        string file, string className, string interfaceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (string.IsNullOrEmpty(interfaceName)) throw new VsmcpException(ErrorCodes.NotFound, "interfaceName is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not in solution: {file}");

        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (sm is null || root is null) throw new VsmcpException(ErrorCodes.NotFound, "Could not parse file.");

        var typeDecl = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"Class '{className}' not in {file}.");

        var typeSym = sm.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol
            ?? throw new VsmcpException(ErrorCodes.NotFound, "Could not resolve class symbol.");

        var compilation = await doc.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new VsmcpException(ErrorCodes.NotFound, "No compilation.");
        var ifaceSym = compilation.GetTypeByMetadataName(interfaceName)
            ?? compilation.GlobalNamespace.GetAllTypesInternal(cancellationToken)
                .FirstOrDefault(t => t.TypeKind == TypeKind.Interface
                    && (t.Name == interfaceName || t.ToDisplayString() == interfaceName))
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"Interface '{interfaceName}' not found.");

        var sb = new StringBuilder();
        foreach (var member in ifaceSym.GetMembers())
        {
            if (member.IsStatic) continue;
            // Skip if already implemented.
            var impl = typeSym.FindImplementationForInterfaceMember(member);
            if (impl is not null && !impl.ContainingType.Equals(typeSym, SymbolEqualityComparer.Default))
            {
                // already has it via base — skip
            }
            else if (impl is not null && impl.ContainingType.Equals(typeSym, SymbolEqualityComparer.Default))
            {
                continue;
            }

            sb.AppendLine(GenerateMemberStub(member));
            sb.AppendLine();
        }

        if (sb.Length == 0)
            return new AddMemberResult { File = file, ClassName = className, InsertedAtLine = -1 };

        return await EditAddMemberAsync(file, className, sb.ToString(), insertBefore: null, openInEditor: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AddMemberResult> CodeOverrideMemberAsync(
        string file, string className, string memberName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");
        if (string.IsNullOrEmpty(memberName)) throw new VsmcpException(ErrorCodes.NotFound, "memberName is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not in solution: {file}");
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (sm is null || root is null) throw new VsmcpException(ErrorCodes.NotFound, "Could not parse file.");

        var typeDecl = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"Class '{className}' not in {file}.");
        var typeSym = sm.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol
            ?? throw new VsmcpException(ErrorCodes.NotFound, "Could not resolve class symbol.");

        ISymbol? toOverride = null;
        var b = typeSym.BaseType;
        while (b is not null && toOverride is null)
        {
            toOverride = b.GetMembers(memberName).FirstOrDefault(m =>
                (m.IsVirtual || m.IsAbstract || m.IsOverride) && !m.IsSealed);
            b = b.BaseType;
        }
        if (toOverride is null)
            throw new VsmcpException(ErrorCodes.NotFound, $"No virtual/abstract member '{memberName}' on a base of {className}.");

        var stub = GenerateMemberStub(toOverride, isOverride: true);
        return await EditAddMemberAsync(file, className, stub, insertBefore: null, openInEditor: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AddMemberResult> CodeGenerateConstructorAsync(
        string file, string className, IReadOnlyList<string>? fromFields, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not in solution: {file}");
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (sm is null || root is null) throw new VsmcpException(ErrorCodes.NotFound, "Could not parse file.");

        var typeDecl = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"Class '{className}' not in {file}.");
        var typeSym = sm.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol
            ?? throw new VsmcpException(ErrorCodes.NotFound, "Could not resolve class symbol.");

        var fields = typeSym.GetMembers().OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !f.IsImplicitlyDeclared)
            .Where(f => fromFields is null || fromFields.Contains(f.Name))
            .Concat<ISymbol>(typeSym.GetMembers().OfType<IPropertySymbol>()
                .Where(p => !p.IsStatic && p.SetMethod is not null && !p.IsImplicitlyDeclared)
                .Where(p => fromFields is null || fromFields.Contains(p.Name)))
            .ToList();

        if (fields.Count == 0)
            throw new VsmcpException(ErrorCodes.NotFound, "No instance fields/properties to bind.");

        var sb = new StringBuilder();
        sb.Append("public ").Append(className).Append('(');
        sb.Append(string.Join(", ", fields.Select(f => $"{TypeOf(f).ToDisplayString()} {Camel(f.Name)}")));
        sb.AppendLine(")");
        sb.AppendLine("{");
        foreach (var f in fields)
            sb.AppendLine($"    this.{f.Name} = {Camel(f.Name)};");
        sb.AppendLine("}");

        return await EditAddMemberAsync(file, className, sb.ToString(), insertBefore: null, openInEditor: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AddMemberResult> CodeGenerateEqualityAsync(
        string file, string className, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(className)) throw new VsmcpException(ErrorCodes.NotFound, "className is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not in solution: {file}");
        var sm = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (sm is null || root is null) throw new VsmcpException(ErrorCodes.NotFound, "Could not parse file.");

        var typeDecl = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"Class '{className}' not in {file}.");
        var typeSym = sm.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol
            ?? throw new VsmcpException(ErrorCodes.NotFound, "Could not resolve class symbol.");

        var fields = typeSym.GetMembers().OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !f.IsImplicitlyDeclared)
            .Concat<ISymbol>(typeSym.GetMembers().OfType<IPropertySymbol>()
                .Where(p => !p.IsStatic && !p.IsImplicitlyDeclared))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"public override bool Equals(object? obj)");
        sb.AppendLine("{");
        sb.AppendLine($"    if (obj is not {className} other) return false;");
        if (fields.Count == 0) sb.AppendLine("    return true;");
        else sb.AppendLine("    return " + string.Join(" && ", fields.Select(f => $"System.Collections.Generic.EqualityComparer<{TypeOf(f).ToDisplayString()}>.Default.Equals(this.{f.Name}, other.{f.Name})")) + ";");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public override int GetHashCode()");
        sb.AppendLine("{");
        if (fields.Count == 0) sb.AppendLine("    return 0;");
        else sb.AppendLine("    return System.HashCode.Combine(" + string.Join(", ", fields.Select(f => "this." + f.Name)) + ");");
        sb.AppendLine("}");

        return await EditAddMemberAsync(file, className, sb.ToString(), insertBefore: null, openInEditor: false, cancellationToken).ConfigureAwait(false);
    }

    // -------- helpers --------

    private static ITypeSymbol TypeOf(ISymbol s) => s switch
    {
        IFieldSymbol f => f.Type,
        IPropertySymbol p => p.Type,
        IMethodSymbol m => m.ReturnType,
        IParameterSymbol pa => pa.Type,
        _ => throw new InvalidOperationException("Unsupported symbol kind."),
    };

    private static string Camel(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var n = name.TrimStart('_');
        if (n.Length == 0) return "value";
        return char.ToLowerInvariant(n[0]) + n.Substring(1);
    }

    private static string GenerateMemberStub(ISymbol member, bool isOverride = false)
    {
        switch (member)
        {
            case IMethodSymbol m:
                {
                    var sb = new StringBuilder();
                    sb.Append(member.DeclaredAccessibility == Accessibility.Public ? "public " : "");
                    if (isOverride) sb.Append("override ");
                    sb.Append(m.ReturnType.ToDisplayString()).Append(' ');
                    sb.Append(m.Name).Append('(');
                    sb.Append(string.Join(", ", m.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}")));
                    sb.Append(") => throw new System.NotImplementedException();");
                    return sb.ToString();
                }
            case IPropertySymbol p:
                {
                    var sb = new StringBuilder();
                    sb.Append(member.DeclaredAccessibility == Accessibility.Public ? "public " : "");
                    if (isOverride) sb.Append("override ");
                    sb.Append(p.Type.ToDisplayString()).Append(' ').Append(p.Name);
                    sb.Append(" { get => throw new System.NotImplementedException();");
                    if (p.SetMethod is not null) sb.Append(" set => throw new System.NotImplementedException();");
                    sb.Append(" }");
                    return sb.ToString();
                }
            case IEventSymbol e:
                return $"public event {e.Type.ToDisplayString()} {e.Name};";
            default:
                return "// unsupported member kind: " + member.Kind;
        }
    }
}
