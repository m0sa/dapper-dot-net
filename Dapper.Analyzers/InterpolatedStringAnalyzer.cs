using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Dapper.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class InterpolatedStringAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "DAPPER0001";
        private const string Category = "Dapper";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, "Interpolated string used in query", "Interpolated string used in query", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterCompilationStartAction(compilationStart =>
            {
                var compilation = compilationStart.Compilation;
                var sqlMapperSymbol = compilation.GetTypeByMetadataName("Dapper.SqlMapper");

                if (sqlMapperSymbol == null) return; // dapper not referenced..

                var methods = sqlMapperSymbol
                    .GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(x => x.IsExtensionMethod)
                    .Where(x => x.Parameters.Any(p => p.Name == "sql" && !p.IsOptional))
                    .ToImmutableHashSet();

                if (methods.IsEmpty) return; // shouldn't happen, but anyways...

                compilationStart.RegisterSyntaxNodeAction(nodeContext =>
                {
                    var invocation = (InvocationExpressionSyntax)nodeContext.Node;
                    if (invocation.ArgumentList.Arguments.Count == 0) return;

                    var model = nodeContext.SemanticModel;
                    var resolved = model.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol;
                    if (resolved == null) return;
                    if (!methods.Contains(resolved))
                    {
                        // the call site is usually uses the reduced form of the ext method, e.g.:
                        // `db.Query("..")` whereas methods contain symbols for `SqlMapper.Query(db, "")`
                        var reducedForm = resolved.GetConstructedReducedFrom();
                        if (reducedForm == null || !methods.Contains(reducedForm)) return;
                    }

                    var sqlParameter = resolved.Parameters.Single(p => p.Name == "sql");

                    // find argument expression for the sqlParameter
                    var arguments = invocation.ArgumentList.Arguments;
                    var firstNamedArgumentIndex = arguments.IndexOf(x => x.NameColon != null);
                    var argumentSyntax =
                        firstNamedArgumentIndex < 0 || sqlParameter.Ordinal < firstNamedArgumentIndex
                            ? arguments[sqlParameter.Ordinal] // no named arguments, or first named argument after the sql parameter's position
                            : arguments.Single(x => x.NameColon.Name.ToString() == "sql");

                    // TODO moar flow analysis...

                    // PERF this could be cached keyed by BlockSyntax
                    var localBlock = new Lazy<SyntaxNode>(() => nodeContext.ContainingSymbol.DeclaringSyntaxReferences.Single().GetSyntax(), false);
                    var assignedOutsideOfDeclaration = new Lazy<ImmutableHashSet<ISymbol>>(() =>
                        Enumerable.Empty<SyntaxNode>()
                            .Concat( // var a = ...; a = b
                                localBlock.Value
                                    .DescendantNodes(x => !x.IsKind(SyntaxKind.VariableDeclaration))
                                    .OfType<AssignmentExpressionSyntax>()
                                    .Select(x => x.Left))
                            .Concat( // var a = ...; test(out/ref a)
                                localBlock.Value
                                    .DescendantNodes()
                                    .OfType<InvocationExpressionSyntax>()
                                    .SelectMany(i => i.ArgumentList.Arguments
                                        .Where(x => x.RefOrOutKeyword.VarianceKindFromToken() != VarianceKind.None)
                                        .Select(x => x.Expression)))
                            .Select(x => model.GetSymbolInfo(x).Symbol)
                            .Where(x => x != null)
                            .ToImmutableHashSet(),
                        false);

                    // currently follows chains of variable-/readonly field declarations with initializers
                    var followSymbol = true;
                    for (var expression = argumentSyntax.Expression; expression != null; )
                    {
                        if (expression.IsKind(SyntaxKind.InterpolatedStringExpression))
                        {
                            nodeContext.ReportDiagnostic(Diagnostic.Create(Rule, expression.GetLocation()));
                            return;
                        }

                        if (model.GetConstantValue(expression).HasValue) return; // we have a const string..
                        if (!followSymbol) return; // don't follow symbols after we've left the local scope


                        var symbol = model.GetSymbolInfo(expression).Symbol;
                        var localSymbol = symbol as ILocalSymbol;
                        var fieldSymbol = symbol as IFieldSymbol;
                        if (localSymbol != null)
                        {
                            followSymbol = true;
                        }
                        else if (fieldSymbol != null)
                        {
                            followSymbol = fieldSymbol.IsReadOnly;
                        }
                        else
                        {
                            followSymbol = false;
                        }

                        var declaration = symbol?.DeclaringSyntaxReferences.Single().GetSyntax() as VariableDeclaratorSyntax;
                        expression = declaration?.Initializer.Value;

                        // TODO see if we can somehow `model.AnalyzeDataFlow()` between (exclusive) declaration and invoication
                        if (symbol != null && declaration != null && assignedOutsideOfDeclaration.Value.Contains(symbol))
                        {
                            return;
                        }
                    }

                }, SyntaxKind.InvocationExpression);
            });
        }
    }
}
