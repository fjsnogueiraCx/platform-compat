﻿using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Terrajobst.Pns.Analyzer.Test.Helpers
{
    public sealed class AnalyzedProject
    {
        private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        private static readonly MetadataReference SystemCoreReference = MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
        private static readonly MetadataReference CSharpSymbolsReference = MetadataReference.CreateFromFile(typeof(CSharpCompilation).Assembly.Location);
        private static readonly MetadataReference CodeAnalysisReference = MetadataReference.CreateFromFile(typeof(Compilation).Assembly.Location);

        private static readonly string DefaultFilePathPrefix = "Test";
        private static readonly string CSharpDefaultFileExt = "cs";
        private static readonly string VisualBasicDefaultExt = "vb";
        private static readonly string TestProjectName = "TestProject";

        private ImmutableArray<AnalyzedDocument> _documents;

        private AnalyzedProject(DiagnosticAnalyzer analyzer, Project project)
        {
            Analyzer = analyzer;
            Project = project;
        }

        public DiagnosticAnalyzer Analyzer { get; }

        public Project Project { get; }

        public ImmutableArray<AnalyzedDocument> Documents
        {
            get
            {
                if (_documents.IsDefault)
                {
                    var documents = AnalyzeDocuments(Analyzer, Project);
                    ImmutableInterlocked.InterlockedInitialize(ref _documents, documents);
                }

                return _documents;
            }
        }

        public static AnalyzedProject Create(DiagnosticAnalyzer analyzer, string language, params string[] sources)
        {
            var project = CreateProject(language, sources);
            return new AnalyzedProject(analyzer, project);
        }

        private static Project CreateProject(string language, string[] sources)
        {
            string fileExtension = language == LanguageNames.CSharp ? CSharpDefaultFileExt : VisualBasicDefaultExt;

            var projectId = ProjectId.CreateNewId(debugName: TestProjectName);

            var solution = new AdhocWorkspace()
                .CurrentSolution
                .AddProject(projectId, TestProjectName, TestProjectName, language)
                .AddMetadataReference(projectId, CorlibReference)
                .AddMetadataReference(projectId, SystemCoreReference)
                .AddMetadataReference(projectId, CSharpSymbolsReference)
                .AddMetadataReference(projectId, CodeAnalysisReference);

            int count = 0;
            foreach (var source in sources)
            {
                var newFileName = $"{DefaultFilePathPrefix}{count}.{fileExtension}";
                var documentId = DocumentId.CreateNewId(projectId, debugName: newFileName);
                var sourceText = SourceText.From(source);
                solution = solution.AddDocument(documentId, newFileName, sourceText);
                count++;
            }

            var project = solution.GetProject(projectId);

            return project;
        }

        private static ImmutableArray<AnalyzedDocument> AnalyzeDocuments(DiagnosticAnalyzer analyzer, Project project)
        {
            var analyzers = ImmutableArray.Create(analyzer);
            var documents = project.Documents;
            var compilation = project.GetCompilationAsync().Result;
            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, project.AnalyzerOptions);
            var diagnostics = compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().Result;

            var documentDiagnostics = diagnostics.Where(d => d.Location.IsInSource)
                                                 .ToLookup(d => project.GetDocument(d.Location.SourceTree));

            return documents.Select(d => new AnalyzedDocument(d, SortDiagnostics(documentDiagnostics[d]).ToImmutableArray()))
                            .ToImmutableArray();

        }

        private static IEnumerable<Diagnostic> SortDiagnostics(IEnumerable<Diagnostic> diangostics)
        {
            return diangostics.OrderBy(d => d.Location.SourceSpan.Start);
        }
    }
}
