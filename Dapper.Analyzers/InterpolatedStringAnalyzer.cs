using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

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
                        if (!methods.Contains(reducedForm)) return;
                    }

                    var sqlParameter = resolved.Parameters.Single(p => p.Name == "sql");

                    // find argument expression for the sqlParameter
                    var arguments = invocation.ArgumentList.Arguments;
                    var firstNamedArgumentIndex = arguments.IndexOf(x => x.NameColon != null);
                    var argumentSyntax =
                        firstNamedArgumentIndex < 0 || sqlParameter.Ordinal < firstNamedArgumentIndex
                            ? arguments[sqlParameter.Ordinal] // no named arguments, or first named argument after the sql parameter's position
                            : arguments.Single(x => x.NameColon.Name.ToString() == "sql");

                    if (model.GetConstantValue(argumentSyntax.Expression).HasValue) return;

                    if (argumentSyntax.Expression.IsKind(SyntaxKind.InterpolatedStringExpression))
                    {
                        nodeContext.ReportDiagnostic(Diagnostic.Create(Rule, argumentSyntax.GetLocation()));
                        return;
                    }

                    // TODO flow analysis...
                    // var flow = model.AnalyzeDataFlow(argumentSyntax.Expression);
                    
                }, SyntaxKind.InvocationExpression);
            });
        }
    }
}
