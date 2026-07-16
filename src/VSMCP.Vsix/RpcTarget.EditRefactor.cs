using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using VSMCP.Core;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    /// <summary>
    /// Move a method (or any single member-of-a-type — including private helpers) from one
    /// partial-class file to another. Same overall shape as <see cref="EditMoveTypeAsync"/>,
    /// but for METHOD nodes inside a parent type rather than top-level type declarations.
    /// </summary>
    public async Task<MoveTypeResult> EditMoveMethodAsync(
        string file, string methodName, string? containerType, string? newFile,
        bool appendIfExists,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(file)) throw new VsmcpException(ErrorCodes.NotFound, "file is required.");
        if (string.IsNullOrEmpty(methodName)) throw new VsmcpException(ErrorCodes.NotFound, "methodName is required.");
        if (string.IsNullOrEmpty(newFile)) throw new VsmcpException(ErrorCodes.NotFound, "newFile is required.");

        var ws = await GetWorkspaceAsync(cancellationToken);
        var doc = FindDocument(ws.CurrentSolution, file)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"File not found in workspace: {file}");

        var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax;
        if (root is null) return new MoveTypeResult { Success = false };

        // Locate the container type (default: first partial class/struct in the file).
        TypeDeclarationSyntax? container = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t =>
                (containerType is null || string.Equals(t.Identifier.Text, containerType, StringComparison.Ordinal)));
        if (container is null) return new MoveTypeResult { Success = false };

        // Find the named member. Methods are most common but a private helper might be a
        // method, property, field, or class — match any MemberDeclarationSyntax with that name.
        MemberDeclarationSyntax? memberDecl = container.Members.FirstOrDefault(m => MemberName(m) == methodName);
        if (memberDecl is null) return new MoveTypeResult { Success = false };

        var targetPath = Path.GetFullPath(newFile!);
        await EnsureWriteAllowedAsync(targetPath, "edit.move_method", cancellationToken).ConfigureAwait(false);
        bool destExists = File.Exists(targetPath);
        if (destExists && !appendIfExists)
            return new MoveTypeResult { Success = false, Conflict = true };

        var srcUsings = root.Usings;
        var srcNs = root.Members.OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        var nsName = srcNs?.Name.ToString();

        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        // If the destination is already a workspace document, its (possibly unsaved) buffer text
        // is the truth; a raw disk read would clobber open edits on apply.
        var destDoc = destExists ? FindDocument(ws.CurrentSolution, targetPath) : null;

        // Build the destination side.
        SyntaxNode destRoot;
        if (destExists)
        {
            var existingText = destDoc is not null
                ? (await destDoc.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString()
                : (await FileReadAsync(targetPath, null, cancellationToken).ConfigureAwait(false)).Content;
            var existingTree = CSharpSyntaxTree.ParseText(existingText, cancellationToken: cancellationToken);
            var existingRoot = await existingTree.GetRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax
                               ?? throw new VsmcpException(ErrorCodes.InteropFault, $"Could not parse '{newFile}' as a C# compilation unit.");

            // Find the matching partial container in the existing destination, walking through the namespace if any.
            TypeDeclarationSyntax? destContainer = existingRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(t => string.Equals(t.Identifier.Text, container.Identifier.Text, StringComparison.Ordinal));

            CompilationUnitSyntax updatedRoot;
            if (destContainer is not null)
            {
                var newDestContainer = destContainer.AddMembers(memberDecl.WithoutLeadingTrivia());
                updatedRoot = existingRoot.ReplaceNode(destContainer, newDestContainer);
            }
            else
            {
                // No matching container in dest — synthesize one alongside whatever's already there.
                var newPartial = MakePartialContainerWith(container, memberDecl);
                updatedRoot = AddMemberToNamespaceOrRoot(existingRoot, nsName, newPartial);
            }

            destRoot = updatedRoot;
        }
        else
        {
            var newContainer = MakePartialContainerWith(container, memberDecl);

            CompilationUnitSyntax newRoot;
            if (!string.IsNullOrEmpty(nsName))
            {
                var ns = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(nsName!))
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newContainer));
                newRoot = SyntaxFactory.CompilationUnit()
                    .WithUsings(srcUsings)
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(ns));
            }
            else
            {
                newRoot = SyntaxFactory.CompilationUnit()
                    .WithUsings(srcUsings)
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newContainer));
            }
            // NormalizeWhitespace on a freshly-built tree gives reasonable formatting.
            destRoot = newRoot.NormalizeWhitespace();
        }

        // Remove the member from the source's container and apply source + destination as ONE
        // workspace transaction where possible — a new file also gets registered with the project.
        var newSourceRoot = root.RemoveNode(memberDecl, SyntaxRemoveOptions.KeepLeadingTrivia)!;
        var newSolution = ws.CurrentSolution.WithDocumentSyntaxRoot(doc.Id, newSourceRoot);

        if (destDoc is not null)
        {
            newSolution = newSolution.WithDocumentSyntaxRoot(destDoc.Id, destRoot);
            RoslynEditor.Apply(ws, newSolution);
        }
        else if (destExists)
        {
            // On disk but not in the workspace (loose file): write it buffer-aware first so a
            // failure leaves at worst a duplicate, never a lost member.
            await FileWriteAsync(targetPath, destRoot.ToFullString(), cancellationToken).ConfigureAwait(false);
            RoslynEditor.Apply(ws, newSolution);
        }
        else
        {
            var newDocId = DocumentId.CreateNewId(doc.Project.Id);
            newSolution = newSolution.AddDocument(newDocId, Path.GetFileName(targetPath),
                SourceText.From(destRoot.ToFullString()), filePath: targetPath);
            RoslynEditor.Apply(ws, newSolution);
        }

        return new MoveTypeResult
        {
            Success = true,
            NewLocation = new CodeSpan { File = targetPath, StartLine = 1, StartColumn = 1, EndLine = 1, EndColumn = 1 },
            Conflict = false,
        };
    }

    private static string? MemberName(MemberDeclarationSyntax m) => m switch
    {
        MethodDeclarationSyntax x => x.Identifier.Text,
        PropertyDeclarationSyntax x => x.Identifier.Text,
        FieldDeclarationSyntax x => x.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        EventDeclarationSyntax x => x.Identifier.Text,
        EventFieldDeclarationSyntax x => x.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        ConstructorDeclarationSyntax x => x.Identifier.Text,
        DestructorDeclarationSyntax x => x.Identifier.Text,
        BaseTypeDeclarationSyntax x => x.Identifier.Text, // nested class/struct/enum
        DelegateDeclarationSyntax x => x.Identifier.Text,
        OperatorDeclarationSyntax x => x.OperatorToken.Text,
        _ => null,
    };

    /// <summary>
    /// Synthesize an empty partial container with the same modifiers/identifier/type-params as
    /// the source, then add the moved member as its sole member.
    /// </summary>
    private static TypeDeclarationSyntax MakePartialContainerWith(TypeDeclarationSyntax sourceContainer, MemberDeclarationSyntax moved)
    {
        // Strip body + members; keep modifiers + identifier + type-parameter list.
        TypeDeclarationSyntax shell = sourceContainer switch
        {
            ClassDeclarationSyntax c => SyntaxFactory.ClassDeclaration(c.Identifier)
                                                     .WithModifiers(c.Modifiers)
                                                     .WithTypeParameterList(c.TypeParameterList),
            StructDeclarationSyntax s => SyntaxFactory.StructDeclaration(s.Identifier)
                                                       .WithModifiers(s.Modifiers)
                                                       .WithTypeParameterList(s.TypeParameterList),
            InterfaceDeclarationSyntax i => SyntaxFactory.InterfaceDeclaration(i.Identifier)
                                                          .WithModifiers(i.Modifiers)
                                                          .WithTypeParameterList(i.TypeParameterList),
            RecordDeclarationSyntax r => SyntaxFactory.RecordDeclaration(r.ClassOrStructKeyword, r.Identifier)
                                                       .WithModifiers(r.Modifiers)
                                                       .WithTypeParameterList(r.TypeParameterList),
            _ => throw new VsmcpException(ErrorCodes.Unsupported, $"Unsupported container kind: {sourceContainer.GetType().Name}"),
        };
        return shell.AddMembers(moved.WithoutLeadingTrivia());
    }

    private static CompilationUnitSyntax AddMemberToNamespaceOrRoot(CompilationUnitSyntax root, string? nsName, MemberDeclarationSyntax member)
    {
        if (string.IsNullOrEmpty(nsName))
            return root.AddMembers(member);

        var ns = root.Members.OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault(n => n.Name.ToString() == nsName);
        if (ns is null)
            return root.AddMembers(member);

        BaseNamespaceDeclarationSyntax updated = ns switch
        {
            NamespaceDeclarationSyntax nds => nds.AddMembers(member),
            FileScopedNamespaceDeclarationSyntax fns => fns.AddMembers(member),
            _ => ns,
        };
        return root.ReplaceNode(ns, updated);
    }

    /// <summary>
    /// Apply a unified diff (#137). Each file patch is read through the live buffer, patched via
    /// the pure <see cref="UnifiedDiff"/> engine, and (unless dryRun) written back through the
    /// editor so VS undo still works. Relative paths in the diff resolve against the open
    /// solution's directory. Per-file isolation: one file's failure doesn't abort the rest.
    /// </summary>
    public async Task<ApplyPatchResult> EditApplyPatchAsync(string unifiedDiff, bool dryRun, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(unifiedDiff))
            throw new VsmcpException(ErrorCodes.NotFound, "unifiedDiff is required.");

        string? baseDir = null;
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE80.DTE2 dte
            && dte.Solution?.FullName is string sln && !string.IsNullOrEmpty(sln))
            baseDir = Path.GetDirectoryName(sln);

        var patches = UnifiedDiff.Parse(unifiedDiff);
        if (patches.Count == 0)
            throw new VsmcpException(ErrorCodes.WrongState, "No file patches were found in the diff.");

        var result = new ApplyPatchResult { Success = true };
        foreach (var patch in patches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = string.IsNullOrEmpty(patch.NewPath) || patch.NewPath == "/dev/null" ? patch.OldPath : patch.NewPath;
            var entry = new ApplyPatchFileResult { Path = rel };
            try
            {
                var full = Path.IsPathRooted(rel) ? rel
                    : baseDir is not null ? Path.GetFullPath(Path.Combine(baseDir, rel))
                    : throw new VsmcpException(ErrorCodes.NotFound, $"Relative path '{rel}' needs an open solution to resolve; use an absolute path.");
                entry.Path = full;

                var current = (await FileReadAsync(full, null, cancellationToken).ConfigureAwait(false)).Content;
                var applied = UnifiedDiff.Apply(current, patch);
                entry.HunksApplied = applied.HunksApplied;
                entry.HunksFailed = applied.HunksFailed;

                if (!applied.Success)
                {
                    entry.Applied = false;
                    entry.Error = string.Join("; ", applied.Failures);
                    result.Success = false;
                }
                else if (dryRun)
                {
                    entry.Applied = false;
                    entry.PreviewText = applied.NewText;
                }
                else
                {
                    await FileWriteAsync(full, applied.NewText!, cancellationToken).ConfigureAwait(false);
                    entry.Applied = true;
                    result.FilesChanged++;
                }
            }
            catch (VsmcpException ex)
            {
                entry.Error = ex.Message;
                result.Success = false;
            }
            catch (Exception ex)
            {
                entry.Error = ex.Message;
                result.Success = false;
                VsmcpLog.Debug("edit.apply_patch", $"failed for {entry.Path}", ex);
            }
            result.Files.Add(entry);
        }
        return result;
    }
}
