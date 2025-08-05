using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace HVO
{
    internal class NamedOneOfReceiver : ISyntaxReceiver
    {
        public List<TypeDeclarationSyntax> Candidates { get; } = new();
        
        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is TypeDeclarationSyntax tds
                && (tds is ClassDeclarationSyntax || tds is StructDeclarationSyntax)
                && tds.AttributeLists.Count > 0
                && tds.Modifiers.Any(m => m.ValueText == "partial"))
            {
                Candidates.Add(tds);
            }
        }
    }
}
