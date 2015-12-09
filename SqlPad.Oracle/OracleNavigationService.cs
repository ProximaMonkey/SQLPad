﻿using System;
using System.Collections.Generic;
using System.Linq;
using SqlPad.Commands;
using SqlPad.Oracle.Commands;
using SqlPad.Oracle.SemanticModel;
using Terminals = SqlPad.Oracle.OracleGrammarDescription.Terminals;
using NonTerminals = SqlPad.Oracle.OracleGrammarDescription.NonTerminals;

namespace SqlPad.Oracle
{
	public class OracleNavigationService : INavigationService
	{
		private static readonly SourcePosition[] EmptyPositionArray = new SourcePosition[0];

		public int? NavigateToQueryBlockRoot(ActionExecutionContext executionContext)
		{
			var documentRepository = executionContext.DocumentRepository;
			var statement = documentRepository.Statements.GetStatementAtPosition(executionContext.CaretOffset);
			if (statement == null)
				return null;

			var semanticModel = (OracleStatementSemanticModel)documentRepository.ValidationModels[statement].SemanticModel;
			var queryBlock = semanticModel.GetQueryBlock(executionContext.CaretOffset);
			return queryBlock?.RootNode.SourcePosition.IndexStart;
		}

		public int? NavigateToDefinition(ActionExecutionContext executionContext)
		{
			var documentRepository = executionContext.DocumentRepository;
			var terminal = documentRepository.Statements.GetTerminalAtPosition(executionContext.CaretOffset, n => !n.Id.IsZeroOffsetTerminalId());
			if (terminal == null || !terminal.Id.In(Terminals.Identifier, Terminals.ObjectIdentifier))
			{
				return null;
			}

			var semanticModel = (OracleStatementSemanticModel)documentRepository.ValidationModels[terminal.Statement].SemanticModel;
			var queryBlock = semanticModel.GetQueryBlock(executionContext.CaretOffset);

			if (queryBlock == null)
			{
				return null;
			}

			switch (terminal.Id)
			{
				case Terminals.Identifier:
					return NavigateToColumnDefinition(queryBlock, terminal);
				case Terminals.ObjectIdentifier:
					return NavigateToObjectDefinition(queryBlock, terminal);
				default:
					throw new NotSupportedException($"Terminal '{terminal.Id}' is not supported. ");
			}
		}

		public IReadOnlyCollection<SourcePosition> FindCorrespondingSegments(ActionExecutionContext executionContext)
		{
			var terminal = executionContext.DocumentRepository.Statements.GetTerminalAtPosition(executionContext.CaretOffset, n => !n.Id.In(Terminals.Semicolon, Terminals.Comma));
			if (terminal == null)
			{
				return EmptyPositionArray;
			}

			var correlatedSegments = FindCorrespondingBeginIfAndCaseTerminals(terminal);
			var semanticModel = (OracleStatementSemanticModel)executionContext.DocumentRepository.ValidationModels[terminal.Statement].SemanticModel;
			correlatedSegments.AddRange(FindCorrespondingInsertColumnAndExpression(semanticModel, terminal));

			return correlatedSegments.AsReadOnly();
		}

		private static IEnumerable<SourcePosition> FindCorrespondingInsertColumnAndExpression(OracleStatementSemanticModel semanticModel, StatementGrammarNode terminal)
		{
			foreach (var insertTarget in semanticModel.InsertTargets)
			{
				if (insertTarget.Columns == null)
				{
					continue;
				}

				int columnIndex;
				if (insertTarget.ValueList != null && insertTarget.ValueList.SourcePosition.Contains(terminal.SourcePosition))
				{
					var valueExpression = FindItemAndIndexIndex(insertTarget.ValueExpressions, e => e.SourcePosition.Contains(terminal.SourcePosition), out columnIndex);
					if (valueExpression != null && insertTarget.Columns.Count > columnIndex)
					{
						yield return valueExpression.SourcePosition;
						yield return GetInsertTargetColumnAtPosition(insertTarget, columnIndex);
					}

					continue;
				}

				Func<OracleSelectListColumn, bool> explicitColumnFilter = c => !c.IsAsterisk && c.HasExplicitDefinition;
				OracleSelectListColumn selectColumn;
				if (insertTarget.ColumnListNode.SourcePosition.Contains(terminal.SourcePosition))
				{
					var columnNode = FindItemAndIndexIndex(insertTarget.Columns.Keys, t => t == terminal, out columnIndex);
					if (columnNode == null)
					{
						continue;
					}

					if (insertTarget.ValueExpressions?.Count > columnIndex)
					{
						yield return columnNode.SourcePosition;
						yield return insertTarget.ValueExpressions[columnIndex].SourcePosition;
					}
					else
					{
						selectColumn = insertTarget.RowSource?.Columns.Where(explicitColumnFilter).Skip(columnIndex).FirstOrDefault();
						if (selectColumn != null)
						{
							yield return columnNode.SourcePosition;
							yield return selectColumn.RootNode.SourcePosition;
						}
					}

					continue;
				}

				if (insertTarget.RowSource != null)
				{
					selectColumn = FindItemAndIndexIndex(insertTarget.RowSource.Columns.Where(explicitColumnFilter), c => c.RootNode.SourcePosition.Contains(terminal.SourcePosition), out columnIndex);
					if (selectColumn != null)
					{
						yield return GetInsertTargetColumnAtPosition(insertTarget, columnIndex);
						yield return selectColumn.RootNode.SourcePosition;
					}
				}
			}
		}

		private static SourcePosition GetInsertTargetColumnAtPosition(OracleInsertTarget insertTarget, int columnIndex)
		{
			return insertTarget.Columns.Keys.Skip(columnIndex).First().SourcePosition;
		}

		private static T FindItemAndIndexIndex<T>(IEnumerable<T> nodes, Func<T, bool> predicate, out int index)
		{
			index = 0;

			foreach (var node in nodes)
			{
				if (predicate(node))
				{
					return node;
				}

				index++;
			}

			return default(T);
		}

		private static List<SourcePosition> FindCorrespondingBeginIfAndCaseTerminals(StatementGrammarNode terminal)
		{
			var correlatedSegments = new List<SourcePosition>();

			if (!terminal.Id.In(Terminals.Case, Terminals.End, Terminals.If, Terminals.Loop, Terminals.Begin) ||
				!terminal.ParentNode.Id.In(NonTerminals.CaseExpression, NonTerminals.PlSqlBasicLoopStatement, NonTerminals.PlSqlIfStatement, NonTerminals.PlSqlCaseStatement, NonTerminals.ProgramEnd, NonTerminals.PackageBodyInitializeSection, NonTerminals.ProgramBody))
			{
				return correlatedSegments;
			}

			var includePreviousChild = false;
			foreach (var child in terminal.ParentNode.ChildNodes)
			{
				if (child.Id.In(Terminals.Case, Terminals.End, Terminals.If, Terminals.Loop))
				{
					if (includePreviousChild)
					{
						var index = correlatedSegments.Count - 1;
						correlatedSegments[index] = SourcePosition.Create(correlatedSegments[index].IndexStart, child.SourcePosition.IndexEnd);
					}
					else
					{
						correlatedSegments.Add(child.SourcePosition);
					}

					if (String.Equals(terminal.ParentNode.Id, NonTerminals.ProgramEnd))
					{
						var begin = terminal.ParentNode.ParentNode[Terminals.Begin];
						if (begin != null)
						{
							correlatedSegments.Add(begin.SourcePosition);
						}
					}

					includePreviousChild = true;
				}
				else if (String.Equals(child.Id, Terminals.Begin))
				{
					var endTerminal = terminal.ParentNode[NonTerminals.ProgramEnd, Terminals.End];
					if (endTerminal != null)
					{
						correlatedSegments.Add(child.SourcePosition);
						correlatedSegments.Add(endTerminal.SourcePosition);
					}

					break;
				}
				else
				{
					includePreviousChild = false;
				}
			}

			if (correlatedSegments.Count == 1)
			{
				correlatedSegments.Clear();
			}

			return correlatedSegments;
		}

		public void FindUsages(ActionExecutionContext executionContext)
		{
			FindUsagesCommand.FindUsages.ExecutionHandler(executionContext);
		}

		public void DisplayBindVariableUsages(ActionExecutionContext executionContext)
		{
			var terminal = executionContext.DocumentRepository.Statements.GetTerminalAtPosition(executionContext.CaretOffset);
			if (terminal == null || !String.Equals(terminal.Id, Terminals.BindVariableIdentifier))
			{
				return;
			}
			
			var nodes = executionContext.DocumentRepository.ValidationModels.Values
				.Select(v => FindUsagesCommand.GetBindVariable((OracleStatementSemanticModel)v.SemanticModel, terminal.Token.Value))
				.Where(v => v != null)
				.SelectMany(v => v.Nodes);

			executionContext.SegmentsToReplace.AddRange(
				nodes.Select(n =>
					new TextSegment
					{
						IndextStart = n.SourcePosition.IndexStart,
						Length = n.SourcePosition.Length,
						DisplayOptions = DisplayOptions.Usage
					}));
		}

		private static int? NavigateToObjectDefinition(OracleQueryBlock queryBlock, StatementGrammarNode terminal)
		{
			var column = queryBlock.AllColumnReferences.SingleOrDefault(c => c.ObjectNode == terminal);

			var dataObjectReference = column?.ValidObjectReference as OracleDataObjectReference;
			
			if (dataObjectReference == null)
				return null;

			var destinationNode = dataObjectReference.AliasNode ?? GetObjectNode(dataObjectReference);
			return destinationNode.SourcePosition.IndexStart;
		}

		private static StatementGrammarNode GetObjectNode(OracleObjectWithColumnsReference dataObjectReference)
		{
			return dataObjectReference.Type == ReferenceType.CommonTableExpression && dataObjectReference.QueryBlocks.Count == 1
				? dataObjectReference.QueryBlocks.First().AliasNode
				: dataObjectReference.ObjectNode;
		}

		private static int? NavigateToColumnDefinition(OracleQueryBlock queryBlock, StatementGrammarNode terminal)
		{
			var column = queryBlock.AllColumnReferences.SingleOrDefault(c => c.ColumnNode == terminal);
			if (column?.ValidObjectReference == null || column.ValidObjectReference.QueryBlocks.Count != 1)
				return null;

			var childQueryBlock = column.ValidObjectReference.QueryBlocks.Single();
			return NavigateThroughQueryBlock(childQueryBlock, column.NormalizedName);
		}

		private static int? NavigateThroughQueryBlock(OracleQueryBlock queryBlock, string normalizedColumnName)
		{
			var selectListColumn = queryBlock.Columns.SingleOrDefault(c => c.NormalizedName == normalizedColumnName);
			if (selectListColumn == null)
				return null;

			if (!selectListColumn.IsDirectReference)
			{
				return selectListColumn.AliasNode?.SourcePosition.IndexStart ?? selectListColumn.RootNode.SourcePosition.IndexStart;
			}

			var columnReference = selectListColumn.ColumnReferences.Single();
			var objectReference = columnReference.ValidObjectReference;
			var isAliasedDirectReference = selectListColumn.AliasNode != columnReference.ColumnNode && !selectListColumn.IsAsterisk;
			if (isAliasedDirectReference || objectReference == null || objectReference.QueryBlocks.Count != 1)
			{
				var destinationNode = selectListColumn.HasExplicitDefinition
					? selectListColumn.AliasNode
					: columnReference.ColumnNode;
				
				return destinationNode.SourcePosition.IndexStart;
			}

			return NavigateThroughQueryBlock(objectReference.QueryBlocks.Single(), normalizedColumnName);
		}
	}
}
