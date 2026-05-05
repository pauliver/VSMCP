using System.Collections.Generic;

namespace VSMCP.Shared
{
    public sealed class AddUsingResult
    {
        public bool Added { get; set; }
        public bool AlreadyPresent { get; set; }
        public int InsertedAtLine { get; set; }
    }
public sealed class RemoveUsingResult
{
    public bool Removed { get; set; }
    public bool WasPresent { get; set; }
}
public sealed class UsingSuggestion
{
    public string SymbolName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public double Confidence { get; set; }
}
public sealed class UsingSuggestionsResult
{
    public List<UsingSuggestion> Suggestions { get; set; } = new();
}
public sealed class AddIncludeResult
{
    public bool Added { get; set; }
    public bool AlreadyPresent { get; set; }
    public int InsertedAtLine { get; set; }
}
}