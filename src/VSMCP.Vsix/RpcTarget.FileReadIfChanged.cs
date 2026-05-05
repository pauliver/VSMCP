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
        public async Task<FileReadIfChangedResult> FileReadIfChangedAsync(string path, string? knownHash, FileRange? range, CancellationToken cancellationToken = default)
        {
            var read = await FileReadAsync(path, range, cancellationToken).ConfigureAwait(false);
            var hash = CtxHelpers.Sha1Hex(read.Content);
            if (!string.IsNullOrEmpty(knownHash) && string.Equals(hash, knownHash, StringComparison.Ordinal))
            {
                return new FileReadIfChangedResult
                {
                    Path = path,
                    Unchanged = true,
                    ContentHash = hash,
                    Result = null,
                };
            }

            return new FileReadIfChangedResult
            {
                Path = path,
                Unchanged = false,
                ContentHash = hash,
                Result = read,
            };
        }
    }
}