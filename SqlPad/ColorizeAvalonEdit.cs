using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace SqlPad
{
	public class ColorizeAvalonEdit : DocumentColorizingTransformer
	{
		private readonly object _lockObject = new object();

		private StatementCollection _statements;
		private readonly Stack<ICollection<TextSegment>> _highlightSegments = new Stack<ICollection<TextSegment>>();
		private readonly List<StatementDescriptionNode> _highlightParenthesis = new List<StatementDescriptionNode>();
		private readonly ISqlParser _parser = ConfigurationProvider.InfrastructureFactory.CreateSqlParser();
		private static readonly SolidColorBrush ErrorBrush = new SolidColorBrush(Colors.Red);
		private static readonly SolidColorBrush HighlightUsageBrush = new SolidColorBrush(Colors.Turquoise);
		private static readonly SolidColorBrush HighlightDefinitionBrush = new SolidColorBrush(Colors.SandyBrown);
		private static readonly SolidColorBrush KeywordBrush = new SolidColorBrush(Colors.Blue);
		private static readonly SolidColorBrush LiteralBrush = new SolidColorBrush(Colors.SaddleBrown/*Color.FromRgb(214, 157, 133)*/);
		private static readonly SolidColorBrush AliasBrush = new SolidColorBrush(Colors.Green);
		private static readonly SolidColorBrush ProgramBrush = new SolidColorBrush(Colors.Magenta);
		private static readonly SolidColorBrush ValidStatementBackgroundBrush = new SolidColorBrush(Color.FromArgb(32, Colors.LightGreen.R, Colors.LightGreen.G, Colors.LightGreen.B));
		private static readonly SolidColorBrush InvalidStatementBackgroundBrush = new SolidColorBrush(Color.FromArgb(32, Colors.PaleVioletRed.R, Colors.PaleVioletRed.G, Colors.PaleVioletRed.B));

		private readonly Dictionary<DocumentLine, ICollection<StatementDescriptionNode>> _lineTerminals = new Dictionary<DocumentLine, ICollection<StatementDescriptionNode>>();
		private readonly Dictionary<DocumentLine, ICollection<StatementDescriptionNode>> _lineNodesWithSemanticErrorsOrInvalidGrammar = new Dictionary<DocumentLine, ICollection<StatementDescriptionNode>>();
		private readonly HashSet<StatementDescriptionNode> _recognizedProgramTerminals = new HashSet<StatementDescriptionNode>();
		private readonly HashSet<StatementDescriptionNode> _unrecognizedTerminals = new HashSet<StatementDescriptionNode>();

		private IDictionary<StatementBase, IValidationModel> _validationModels;
		
		public IList<StatementDescriptionNode> HighlightParenthesis { get { return _highlightParenthesis.AsReadOnly(); } }
		
		public IEnumerable<TextSegment> HighlightSegments { get { return _highlightSegments.SelectMany(c => c); } }

		public void SetStatementCollection(SqlDocumentRepository documentRepository)
		{
			if (documentRepository == null)
				return;

			lock (_lockObject)
			{
				_statements = documentRepository.Statements;

				_validationModels = documentRepository.ValidationModels;

				ClearNodeIndexes();
			}
		}

		private void ClearNodeIndexes()
		{
			_lineTerminals.Clear();
			_recognizedProgramTerminals.Clear();
			_unrecognizedTerminals.Clear();
			_lineNodesWithSemanticErrorsOrInvalidGrammar.Clear();
		}

		public void SetHighlightParenthesis(ICollection<StatementDescriptionNode> parenthesisNodes)
		{
			_highlightParenthesis.Clear();
			_highlightParenthesis.AddRange(parenthesisNodes);
		}

		public void SetHighlightSegments(ICollection<TextSegment> highlightSegments)
		{
			lock (_lockObject)
			{
				if (highlightSegments != null)
				{
					if (highlightSegments.Count == 0 ||
					    _highlightSegments.SelectMany(c => c).Contains(highlightSegments.First()))
						return;

					_highlightSegments.Push(highlightSegments);
				}
				else if (_highlightSegments.Count > 0)
				{
					_highlightSegments.Pop();
				}
			}
		}

		protected override void Colorize(ITextRunConstructionContext context)
		{
			lock (_lockObject)
			{
				if (_statements == null)
					return;

				BuildNodeIndexes(context);

				base.Colorize(context);
			}
		}

		private void BuildNodeIndexes(ITextRunConstructionContext context)
		{
			if (_lineTerminals.Count > 0)
				return;
			
			BuildLineTerminalDictionary(context);

			BuildProgramTerminalHashset();

			BuildUnrecognizedTerminalHashset();

			BuildLineNodeWithSemanticErrorOrInvalidGrammarDictionary(context);
		
		}

		private void BuildLineNodeWithSemanticErrorOrInvalidGrammarDictionary(ITextRunConstructionContext context)
		{
			var semanticErrorOrInvalidGrammarNodeEnumerator = _validationModels.Values
				.SelectMany(vm => vm.GetNodesWithSemanticErrors())
				.Select(kvp => kvp.Key)
				.Concat(_statements.SelectMany(s => s.InvalidGrammarNodes))
				.OrderBy(n => n.SourcePosition.IndexStart)
				.GetEnumerator();

			BuildLineNodeDictionary(semanticErrorOrInvalidGrammarNodeEnumerator, context, _lineNodesWithSemanticErrorsOrInvalidGrammar);
		}

		private void BuildUnrecognizedTerminalHashset()
		{
			var notRecognizedTerminals = _validationModels.Values
				.SelectMany(vm => vm.ObjectNodeValidity.Concat(vm.ProgramNodeValidity).Concat(vm.ColumnNodeValidity))
				.Where(kvp => !kvp.Value.IsRecognized)
				.Select(kvp => kvp.Key);

			_unrecognizedTerminals.AddRange(notRecognizedTerminals);
		}

		private void BuildProgramTerminalHashset()
		{
			var recognizedProgramTerminalEnumerator = _validationModels.Values
				.SelectMany(vm => vm.ProgramNodeValidity)
				.Where(kvp => kvp.Value.IsRecognized && kvp.Key.Type == NodeType.Terminal)
				.Select(kvp => kvp.Key);

			_recognizedProgramTerminals.AddRange(recognizedProgramTerminalEnumerator);
		}

		private void BuildLineTerminalDictionary(ITextRunConstructionContext context)
		{
			var terminalEnumerator = _statements.SelectMany(s => s.AllTerminals).GetEnumerator();
			BuildLineNodeDictionary(terminalEnumerator, context, _lineTerminals);
		}

		private static void BuildLineNodeDictionary(IEnumerator<StatementDescriptionNode> nodeEnumerator, ITextRunConstructionContext context, Dictionary<DocumentLine, ICollection<StatementDescriptionNode>> dictionary)
		{
			if (!nodeEnumerator.MoveNext())
				return;

			foreach (var line in context.Document.Lines)
			{
				var singleLineTerminals = new List<StatementDescriptionNode>();
				dictionary.Add(line, singleLineTerminals);

				do
				{
					if (line.EndOffset < nodeEnumerator.Current.SourcePosition.IndexStart)
						break;

					singleLineTerminals.Add(nodeEnumerator.Current);
				}
				while (nodeEnumerator.MoveNext());
			}
		}

		protected override void ColorizeLine(DocumentLine line)
		{
			if (_statements == null)
				return;

			ICollection<StatementDescriptionNode> lineTerminals;
			if (_lineTerminals.TryGetValue(line, out lineTerminals))
			{
				foreach (var terminal in lineTerminals)
				{
					SolidColorBrush brush = null;
					if (_parser.IsKeyword(terminal.Token.Value))
						brush = KeywordBrush;
					else if (_parser.IsLiteral(terminal.Id))
						brush = LiteralBrush;
					else if (_parser.IsAlias(terminal.Id))
						brush = AliasBrush;
					else if (_recognizedProgramTerminals.Contains(terminal))
						brush = ProgramBrush;
					else if (_unrecognizedTerminals.Contains(terminal))
						brush = ErrorBrush;

					if (brush == null)
						continue;

					ProcessNodeAtLine(line, terminal.SourcePosition,
						element => element.TextRunProperties.SetForegroundBrush(brush));
				}
			}

			ProcessNodeCollectionAtLine(line, _lineNodesWithSemanticErrorsOrInvalidGrammar,
				element => element.TextRunProperties.SetTextDecorations(Resources.WaveErrorUnderline));

			foreach (var parenthesisNode in _highlightParenthesis)
			{
				ProcessNodeAtLine(line, parenthesisNode.SourcePosition, element => element.TextRunProperties.SetTextDecorations(TextDecorations.Underline));
			}

			var statementsAtLine = _statements.Where(s => s.SourcePosition.IndexStart <= line.EndOffset && s.SourcePosition.IndexEnd >= line.Offset);

			foreach (var statement in statementsAtLine)
			{
				var backgroundColor = statement.ProcessingStatus == ProcessingStatus.Success ? ValidStatementBackgroundBrush : InvalidStatementBackgroundBrush;

				var colorStartOffset = Math.Max(line.Offset, statement.SourcePosition.IndexStart);
				var colorEndOffset = Math.Min(line.EndOffset, statement.SourcePosition.IndexEnd + 1);

				ChangeLinePart(
					colorStartOffset,
					colorEndOffset,
					element =>
					{
						element.BackgroundBrush = backgroundColor;

						//ProcessNodeAtLine(line, semanticError.Node.SourcePosition,
						//	element => element.TextRunProperties.SetTextDecorations(Resources.BoxedText));

						/*ProcessNodeAtLine(line, nodeSemanticError.Key.SourcePosition,
							element =>
							{
								element.BackgroundBrush = Resources.OutlineBoxBrush;
								var x = 1;
							});*/

						/*
						// This lambda gets called once for every VisualLineElement
						// between the specified offsets.
						var tf = element.TextRunProperties.Typeface;
						// Replace the typeface with a modified version of
						// the same typeface
						element.TextRunProperties.SetTypeface(new Typeface(
							tf.FontFamily,
							FontStyles.Italic,
							FontWeights.Bold,
							tf.Stretch
						));*/
					});

				foreach (var highlightSegment in _highlightSegments.SelectMany(s => s))
				{
					ProcessNodeAtLine(line,
						new SourcePosition { IndexStart = highlightSegment.IndextStart, IndexEnd = highlightSegment.IndextStart + highlightSegment.Length - 1 },
						element => element.BackgroundBrush = highlightSegment.DisplayOptions == DisplayOptions.Usage ? HighlightUsageBrush : HighlightDefinitionBrush);
				}
			}
		}

		private void ProcessNodeCollectionAtLine(DocumentLine line, IReadOnlyDictionary<DocumentLine, ICollection<StatementDescriptionNode>> lineNodeDictionary, Action<VisualLineElement> visualElementAction)
		{
			ICollection<StatementDescriptionNode> nodes;
			if (!lineNodeDictionary.TryGetValue(line, out nodes))
				return;
			
			foreach (var node in nodes)
			{
				ProcessNodeAtLine(line, node.SourcePosition, visualElementAction);
			}
		}

		private void ProcessNodeAtLine(ISegment line, SourcePosition nodePosition, Action<VisualLineElement> visualElementAction)
		{
			if (line.Offset > nodePosition.IndexEnd + 1 ||
			    line.EndOffset < nodePosition.IndexStart)
				return;

			var errorColorStartOffset = Math.Max(line.Offset, nodePosition.IndexStart);
			var errorColorEndOffset = Math.Min(line.EndOffset, nodePosition.IndexEnd + 1);

			ChangeLinePart(errorColorStartOffset, errorColorEndOffset, visualElementAction);
		}
	}
}
