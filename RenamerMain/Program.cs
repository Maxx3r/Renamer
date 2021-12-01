using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Rename;

namespace Rewriter
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Roslyn Renamer example by @_xpn_\n");

            Program p = new Program();

            if (args.Length != 1)
            {
                Console.WriteLine("[*] Error: No solution path provided");
                return;
            }
            p.Run(args[0]).Wait();
        }

        // via SO: https://stackoverflow.com/questions/1344221/how-can-i-generate-random-alphanumeric-strings
        public static string RandomString(int length)
        {
            var random = new Random();

            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        MSBuildWorkspace m_workspace;

        public async Task Run(string solutionPath)
        {
            // Create a workspace to allow loading our solution
            m_workspace = MSBuildWorkspace.Create();

            // Load our target solution
            Solution solution = await m_workspace.OpenSolutionAsync(solutionPath);

            // Find all projects in the solution
            var projects = solution.Projects;

            // Get the first project
            var project = projects.FirstOrDefault();

            // Enumerate through documents in the project
            foreach (var document in project.Documents)
            {
                // Get our syntax tree representation of the document
                var syntaxTree = await document.GetSyntaxTreeAsync();

                // Find all classes and pass them to be renamed
                var classes = syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>();
                foreach (var c in classes)
                {
                    solution = await Replace<ClassDeclarationSyntax>(solution, project.Name, document.Name, c.Identifier.ToString(), RandomString(10));
                }

                // Find all enums and pass them to be renamed
                var enums = syntaxTree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>();
                foreach (var e in enums)
                {
                    solution = await Replace<EnumDeclarationSyntax>(solution, project.Name, document.Name, e.Identifier.ToString(), RandomString(10));
                }

                // Find all namespaces and pass them to be renamed
                var namespaces = syntaxTree.GetRoot().DescendantNodes().OfType<NamespaceDeclarationSyntax>();
                foreach (var n in namespaces)
                {
                    solution = await Replace<NamespaceDeclarationSyntax>(solution, project.Name, document.Name, n.Name.ToString(), RandomString(10));
                }
            }

            // When completed, apply changes to our solution
            m_workspace.TryApplyChanges(solution);
        }

        public async Task<Solution> Replace<T>(Solution solution, string projectName, string documentName, string oldName, string newName)
        {
            ISymbol symbol;
            IEnumerable<T> nodes;

            var project = solution.Projects.Where(s => s.Name == projectName).FirstOrDefault();
            var document = project.Documents.Where(s => s.Name == documentName).FirstOrDefault();
            var semanticModel = await document.GetSemanticModelAsync();
            var syntaxTree = await document.GetSyntaxTreeAsync();

            // Check if we have a namespace as this has a different property to search for
            if (typeof(T) == typeof(NamespaceDeclarationSyntax))
            {
                nodes = (IEnumerable<T>)syntaxTree.GetRoot().DescendantNodes().OfType<T>();
                var node = ((IEnumerable<NamespaceDeclarationSyntax>)nodes).Where(s => s.Name.ToString() == oldName).FirstOrDefault();
                if (node == null)
                {
                    return solution;
                }

                // Get a symbol for our namespace
                symbol = semanticModel.GetDeclaredSymbol(node);
            }
            else
            {
                nodes = (IEnumerable<T>)syntaxTree.GetRoot().DescendantNodes().OfType<T>();
                var node = ((IEnumerable<BaseTypeDeclarationSyntax>)nodes).Where(s => s.Identifier.ToString() == oldName).FirstOrDefault();
                if (node == null)
                {
                    return solution;
                }

                // Get a symbol for our class/enum
                symbol = semanticModel.GetDeclaredSymbol(node);
            }

            // Use Roslyn to rename throughout our solution
            solution = await Renamer.RenameSymbolAsync(solution, symbol, newName, solution.Workspace.Options);

            return solution;
        }
    }
}