using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using TestHelper;
using Dapper;
using Dapper.Analyzers;

namespace Dapper.Tests.Analyzers
{
    [TestClass]
    public class UnitTest : CodeFixVerifier
    {

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer() => new InterpolatedStringAnalyzer();

        [TestMethod]
        public void NoDiagnostics_Empty() => VerifyCSharpDiagnostic(@"");

        [TestMethod]
        public void NoDiagnostics_Const()
        {
            var test = @"
    namespace TestMethod1
    {
        using System;
        using System.Data;
        using Dapper;
        class TypeName
        {
            public static void Main()
            {
                IDbConnection db = null;
                const string sql = ""select 1"";
                db.Query(sql);
            }
        }
    }";
            VerifyCSharpDiagnostic(test);
        }

        [TestMethod]
        public void NoDiagnostics_Const_Flow()
        {
            var test = @"
    namespace TestMethod1
    {
        using System;
        using System.Data;
        using Dapper;
        class TypeName
        {
            public static void Main()
            {
                IDbConnection db = null;
                var sql = ""select 1"";
                db.Query(sql);
            }
        }
    }";
            VerifyCSharpDiagnostic(test);
        }
        
        [TestMethod]
        public void Diagnostic_On_InterpolatedString()
        {
            var test = @"
    namespace TestMethod2
    {
        using System;
        using System.Data;
        using Dapper;
        class TypeName
        {
            public static void Main()
            {
                IDbConnection db = null;
                int id = -1;
                db.Query($""select * from Users where Id={id}"");
            }
        }
    }";
            var expected = new DiagnosticResult
            {
                Id = "DAPPER0001",
                Message = "Interpolated string used in query",
                Severity = DiagnosticSeverity.Warning,
                Locations =
                    new[]
                    {
                        new DiagnosticResultLocation("Test0.cs", 13, 26)
                    }
            };

            VerifyCSharpDiagnostic(test, expected);
        }
        
        [TestMethod]
        public void Diagnostic_On_InterpolatedString_Flow()
        {
            var test = @"
    namespace TestMethod2
    {
        using System;
        using System.Data;
        using Dapper;
        class TypeName
        {
            public static void Main()
            {
                IDbConnection db = null;
                int id = -1;
                var sql1 = $""select {id}"";
                var sql2 = sql1;
                db.Query(sql2);
            }
        }
    }";
            var expected = new DiagnosticResult
            {
                Id = "DAPPER0001",
                Message = "Interpolated string used in query",
                Severity = DiagnosticSeverity.Warning,
                Locations =
                    new[]
                    {
                        new DiagnosticResultLocation("Test0.cs", 13, 28)
                    }
            };

            VerifyCSharpDiagnostic(test, expected);
        }

        [TestMethod]
        public void NoDiagnostic_On_Const_Flow()
        {
            var test = @"
    namespace TestMethod2
    {
        using System;
        using System.Data;
        using Dapper;
        class TypeName
        {
            private const string sql0 = ""select 1"";
            public static void Main()
            {
                IDbConnection db = null;
                var sql1 = sql0;
                var sql2 = sql1;
                db.Query(sql2);
            }
        }
    }";

            VerifyCSharpDiagnostic(test);
        }

        [TestMethod]
        public void Diagnostic_On_InterpolatedString_Field_Flow()
        {
            var test = @"
    namespace TestMethod2
    {
        using System;
        using System.Data;
        using Dapper;
        class TypeName
        {
            public static string sql0 = $""select {id}"";
            public static void Main()
            {
                IDbConnection db = null;
                int id = -1;
                var sql1 = sql0;
                var sql2 = sql1;
                db.Query(sql2);
            }
        }
    }";
            var expected = new DiagnosticResult
            {
                Id = "DAPPER0001",
                Message = "Interpolated string used in query",
                Severity = DiagnosticSeverity.Warning,
                Locations =
                    new[]
                    {
                        new DiagnosticResultLocation("Test0.cs", 9, 41)
                    }
            };

            VerifyCSharpDiagnostic(test, expected);
        }
    }
}