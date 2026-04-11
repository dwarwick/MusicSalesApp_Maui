// Stub for QueryPropertyAttribute so that ViewModels using [QueryProperty]
// can compile in the plain .NET test project (no MAUI dependency).
// The real attribute lives in Microsoft.Maui.Controls.
namespace Microsoft.Maui.Controls;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class QueryPropertyAttribute : Attribute
{
    public QueryPropertyAttribute(string name, string queryId) { }
}
