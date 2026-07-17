using System;
using System.Collections.Generic;
using System.IO;

namespace VSMCP.Core;

/// <summary>
/// THE C/C++ source-file extension set. Both the generic file tools' supplemental disk scan and
/// the cpp search/index walkers must agree on what counts as a C++ file — two drifting sets meant
/// file_list could see files the cpp tools were blind to (and vice versa).
/// </summary>
public static class CppFileTypes
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".h", ".hpp", ".hxx", ".hh", ".inl", ".c", ".cpp", ".cc", ".cxx",
    };

    public static bool IsCppFile(string path) => Extensions.Contains(Path.GetExtension(path));
}
