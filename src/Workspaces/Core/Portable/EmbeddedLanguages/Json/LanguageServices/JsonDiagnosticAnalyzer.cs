﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.EmbeddedLanguages.LanguageServices;
using Microsoft.CodeAnalysis.EmbeddedLanguages.VirtualChars;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.EmbeddedLanguages.Json.LanguageServices
{
    internal class JsonDiagnosticAnalyzer : IEmbeddedDiagnosticAnalyzer
    {
        private readonly int _stringLiteralKind;
        private readonly ISyntaxFactsService _syntaxFacts;
        private readonly ISemanticFactsService _semanticFacts;
        private readonly IVirtualCharService _virtualCharService;

        private readonly DiagnosticDescriptor _descriptor;

        public JsonDiagnosticAnalyzer(
            int stringLiteralKind,
            ISyntaxFactsService syntaxFacts,
            ISemanticFactsService semanticFacts,
            IVirtualCharService virtualCharService)
        {
            _stringLiteralKind = stringLiteralKind;
            _syntaxFacts = syntaxFacts;
            _semanticFacts = semanticFacts;
            _virtualCharService = virtualCharService;

            _descriptor = new DiagnosticDescriptor("JSON001",
                new LocalizableResourceString(nameof(WorkspacesResources.JSON_issue_0), WorkspacesResources.ResourceManager, typeof(WorkspacesResources)),
                new LocalizableResourceString(nameof(WorkspacesResources.JSON_issue_0), WorkspacesResources.ResourceManager, typeof(WorkspacesResources)),
                WorkspacesResources.Style,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);

            SupportedDiagnostics = ImmutableArray.Create(_descriptor);
        }

        public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }

        public void Analyze(SemanticModelAnalysisContext context, OptionSet optionSet)
        {
            var semanticModel = context.SemanticModel;
            var syntaxTree = semanticModel.SyntaxTree;
            var cancellationToken = context.CancellationToken;
            var options = context.Options;

            var option = optionSet.GetOption(JsonOptions.ReportInvalidJsonPatterns, syntaxTree.Options.Language);
            if (!option)
            {
                return;
            }

            var detector = JsonPatternDetector.GetOrCreate(
                semanticModel, _syntaxFacts, _semanticFacts, _virtualCharService);

            var root = syntaxTree.GetRoot(cancellationToken);
            Analyze(context, detector, root, cancellationToken);
        }

        private void Analyze(
            SemanticModelAnalysisContext context, JsonPatternDetector detector,
            SyntaxNode node, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var child in node.ChildNodesAndTokens())
            {
                if (child.IsNode)
                {
                    Analyze(context, detector, child.AsNode(), cancellationToken);
                }
                else
                {
                    var token = child.AsToken();
                    if (token.RawKind == _stringLiteralKind &&
                        detector.IsDefinitelyJson(token, cancellationToken))
                    {
                        var tree = detector.TryParseJson(token);
                        if (tree != null)
                        {
                            foreach (var diag in tree.Diagnostics)
                            {
                                context.ReportDiagnostic(Diagnostic.Create(
                                    _descriptor,
                                    Location.Create(context.SemanticModel.SyntaxTree, diag.Span),
                                    diag.Message));
                            }
                        }
                    }
                }
            }
        }
    }
}
