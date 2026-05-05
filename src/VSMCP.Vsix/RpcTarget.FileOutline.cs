using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix
{
    internal sealed partial class RpcTarget
    {
        public async Task<FileOutlineResult> FileOutlineAsync(string file, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(file))
                throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
            var read = await FileReadAsync(file, null, cancellationToken).ConfigureAwait(false);
            var hash = CtxHelpers.Sha1Hex(read.Content);
            var result = new FileOutlineResult
            {
                File = file,
                ContentHash = hash
            };
            // Refuse non-C# files outright â€” outline only works for Roslyn-supported languages.
            // Previously fell through to a "dump every line" branch that was strictly worse than
            // file_read (#93). C++ callers should use cpp_header_lookup / cpp_include_chain instead.
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".cs" && ext != ".csx")
            {
                throw new VsmcpException(ErrorCodes.Unsupported, $"file_outline only supports C# (.cs/.csx) files; '{ext}' is not Roslyn-parseable. " + "For C/C++ headers, use cpp_header_lookup or cpp_include_chain.");
            }

            // Try the live workspace first; fall back to standalone parse for files not in any
            // loaded project (Open Folder mode, <MiscFiles>, ad-hoc scripts) â€” fixes #96.
            CompilationUnitSyntax? root = null;
            var ws = await GetWorkspaceAsync(cancellationToken);
            var doc = FindDocumentAnywhere(ws.CurrentSolution, file);
            if (doc is not null)
                root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax;
            if (root is null)
            {
                var tree = CSharpSyntaxTree.ParseText(read.Content, cancellationToken: cancellationToken);
                root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax;
            }

            if (root is null)
            {
                throw new VsmcpException(ErrorCodes.Unsupported, $"Could not parse '{file}' as a C# compilation unit.");
            }

            // Usings + namespace header verbatim; types abbreviated; members signature-only with line markers.
            foreach (var u in root.Usings)
                result.Lines.Add(u.ToString().TrimEnd());
            if (root.Usings.Count > 0)
                result.Lines.Add("");
            foreach (var member in root.Members)
                AppendOutlineNode(member, result.Lines, indent: 0);
            return result;
        }
private static void AppendOutlineNode(MemberDeclarationSyntax node, List<string> into, int indent)
    {
        var pad = new string(' ', indent * 4);
        if (node is BaseNamespaceDeclarationSyntax ns)
        {
            into.Add($"{pad}namespace {ns.Name}");
            into.Add($"{pad}{{");
            foreach (var m in ns.Members) AppendOutlineNode(m, into, indent + 1);
            into.Add($"{pad}}}");
            return;
        }
        if (node is FileScopedNamespaceDeclarationSyntax fns)
        {
            into.Add($"{pad}namespace {fns.Name};");
            into.Add("");
            foreach (var m in fns.Members) AppendOutlineNode(m, into, indent);
            return;
        }
        if (node is BaseTypeDeclarationSyntax type)
        {
            var line = type.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var hdr = ExtractTypeHeader(type);
            into.Add($"{pad}{hdr}    // L{line}");
            into.Add($"{pad}{{");
            if (type is TypeDeclarationSyntax td)
                foreach (var member in td.Members) AppendOutlineNode(member, into, indent + 1);
            into.Add($"{pad}}}");
            return;
        }
        // Methods, properties, fields, etc.
        var memberLine = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var sig = ExtractMemberSignature(node);
        into.Add($"{pad}{sig}    // L{memberLine}");
    }
private static string ExtractTypeHeader(BaseTypeDeclarationSyntax type)
    {
        var modifiers = string.Join(" ", type.Modifiers.Select(m => m.Text));
        var keyword = type switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax r => r.ClassOrStructKeyword.Text == "" ? "record" : $"record {r.ClassOrStructKeyword.Text}",
            EnumDeclarationSyntax => "enum",
            _ => "type",
        };
        var bases = type.BaseList?.ToString() ?? "";
        var ident = type.Identifier.Text;
        var generics = (type as TypeDeclarationSyntax)?.TypeParameterList?.ToString() ?? "";
        return $"{modifiers} {keyword} {ident}{generics} {bases}".Trim();
    }
private static string ExtractMemberSignature(MemberDeclarationSyntax node) => node switch
    {
        MethodDeclarationSyntax m => $"{string.Join(" ", m.Modifiers.Select(x => x.Text))} {m.ReturnType} {m.Identifier}{m.TypeParameterList}{m.ParameterList} {{ ... }}".Trim(),
        ConstructorDeclarationSyntax c => $"{string.Join(" ", c.Modifiers.Select(x => x.Text))} {c.Identifier}{c.ParameterList} {{ ... }}".Trim(),
        DestructorDeclarationSyntax d => $"~{d.Identifier}() {{ ... }}",
        PropertyDeclarationSyntax p => $"{string.Join(" ", p.Modifiers.Select(x => x.Text))} {p.Type} {p.Identifier} {{ ... }}".Trim(),
        FieldDeclarationSyntax f => $"{string.Join(" ", f.Modifiers.Select(x => x.Text))} {f.Declaration};".Trim(),
        EventDeclarationSyntax e => $"{string.Join(" ", e.Modifiers.Select(x => x.Text))} event {e.Type} {e.Identifier};".Trim(),
        EventFieldDeclarationSyntax ef => $"{string.Join(" ", ef.Modifiers.Select(x => x.Text))} event {ef.Declaration};".Trim(),
        DelegateDeclarationSyntax dl => $"{string.Join(" ", dl.Modifiers.Select(x => x.Text))} delegate {dl.ReturnType} {dl.Identifier}{dl.ParameterList};".Trim(),
        OperatorDeclarationSyntax op => $"{string.Join(" ", op.Modifiers.Select(x => x.Text))} {op.ReturnType} operator {op.OperatorToken}{op.ParameterList} {{ ... }}".Trim(),
        ConversionOperatorDeclarationSyntax co => $"{string.Join(" ", co.Modifiers.Select(x => x.Text))} {co.ImplicitOrExplicitKeyword} operator {co.Type}{co.ParameterList} {{ ... }}".Trim(),
        IndexerDeclarationSyntax ix => $"{string.Join(" ", ix.Modifiers.Select(x => x.Text))} {ix.Type} this{ix.ParameterList} {{ ... }}".Trim(),
        EnumMemberDeclarationSyntax em => $"{em.Identifier},",
        _ => node.ToString().Split('\n')[0].Trim() + " ...",
    };
    }
}