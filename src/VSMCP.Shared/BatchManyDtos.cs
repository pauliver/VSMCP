using System.Collections.Generic;

namespace VSMCP.Shared;

// ---- C++ batch item DTOs ----

public sealed class CppReadMemberItem
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MemberName { get; set; } = "";
}

public sealed class CppReplaceMemberItem
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MemberName { get; set; } = "";
    public string NewCode { get; set; } = "";
}

public sealed class CppUnsavedBufferItem
{
    public string File { get; set; } = "";
    public string? Content { get; set; }
}

public sealed class CppRenameItem
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string NewName { get; set; } = "";
}

public sealed class CppMoveTypeItem
{
    public string SourceFile { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string TargetFile { get; set; } = "";
    public bool CreateTargetIfMissing { get; set; } = true;
}

public sealed class CppMoveMethodItem
{
    public string SourceFile { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string TargetFile { get; set; } = "";
    public bool CreateTargetIfMissing { get; set; } = true;
}

public sealed class CppImplementInterfaceItem
{
    public string DerivedFile { get; set; } = "";
    public string DerivedClass { get; set; } = "";
    public string BaseClass { get; set; } = "";
    public string? BaseClassFile { get; set; }
}

public sealed class CppGenerateCtorItem
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public IReadOnlyList<string>? MemberNames { get; set; }
}

public sealed class CppOverrideMemberItem
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string ReturnType { get; set; } = "";
    public string Parameters { get; set; } = "";
}

public sealed class CppGenerateEqualityItem
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
}

// ---- C# / Roslyn batch item DTOs ----

public sealed class EditReplaceMemberItem
{
    public string File { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string MemberName { get; set; } = "";
    public string NewCode { get; set; } = "";
}

public sealed class EditMoveTypeItem
{
    public string File { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string NewFile { get; set; } = "";
    public string? NewNamespace { get; set; }
    public bool AppendIfExists { get; set; }
}

public sealed class EditMoveMethodItem
{
    public string File { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string NewFile { get; set; } = "";
    public string? ContainerType { get; set; }
    public bool AppendIfExists { get; set; }
}

public sealed class EditRenameItem
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string NewName { get; set; } = "";
}

public sealed class EditAddUsingItem
{
    public string File { get; set; } = "";
    public string Namespace { get; set; } = "";
}

public sealed class EditAddIncludeItem
{
    public string File { get; set; } = "";
    public string IncludePath { get; set; } = "";
    public bool System { get; set; }
}

public sealed class CodeImplementInterfaceItem
{
    public string DerivedFile { get; set; } = "";
    public string DerivedClass { get; set; } = "";
    public string BaseClass { get; set; } = "";
}

public sealed class CodeOverrideMemberItem
{
    public string File { get; set; } = "";
    public string DerivedClass { get; set; } = "";
    public string BaseMember { get; set; } = "";
}

public sealed class CodeGenerateCtorItem
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public IReadOnlyList<string>? MemberNames { get; set; }
}

public sealed class CodeGenerateEqualityItem
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
}

// ---- File-side batch DTOs ----

public sealed class FileMembersItem
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
}

public sealed class FileReadIfChangedItem
{
    public string File { get; set; } = "";
    public long SinceTimestampMs { get; set; }
}
