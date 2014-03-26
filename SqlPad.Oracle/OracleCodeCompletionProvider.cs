﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NonTerminals = SqlPad.Oracle.OracleGrammarDescription.NonTerminals;
using Terminals = SqlPad.Oracle.OracleGrammarDescription.Terminals;

namespace SqlPad.Oracle
{
	public class OracleCodeCompletionProvider : ICodeCompletionProvider
	{
		private readonly OracleSqlParser _oracleParser = new OracleSqlParser();
		private static readonly ICodeCompletionItem[] EmptyCollection = new ICodeCompletionItem[0];
		private const string CategoryJoinType = "Join Type";

		private static readonly OracleCodeCompletionItem[] JoinClauses =
		{
			new OracleCodeCompletionItem { Name = "JOIN", Priority = 0, Category = CategoryJoinType, CategoryPriority = 1 },
			new OracleCodeCompletionItem { Name = "LEFT JOIN", Priority = 1, Category = CategoryJoinType, CategoryPriority = 1 },
			new OracleCodeCompletionItem { Name = "RIGHT JOIN", Priority = 2, Category = CategoryJoinType, CategoryPriority = 1 },
			new OracleCodeCompletionItem { Name = "FULL JOIN", Priority = 3, Category = CategoryJoinType, CategoryPriority = 1 },
		};

		public ICollection<ICodeCompletionItem> ResolveItems(string statementText, int cursorPosition)
		{
			//Trace.WriteLine("OracleCodeCompletionProvider.ResolveItems called. Cursor position: "+ cursorPosition);

			OracleStatementSemanticModel semanticModel;
			var databaseModel = DatabaseModelFake.Instance;

			var statements = _oracleParser.Parse(statementText);
			var statement = (OracleStatement)statements.SingleOrDefault(s => s.GetNodeAtPosition(cursorPosition) != null);
			if (statement == null)
			{
				if (statements.Count > 0)
				{
					var lastStatement = (OracleStatement)statements.Last(s => s.GetNearestTerminalToPosition(cursorPosition) != null);
					semanticModel = new OracleStatementSemanticModel(null, lastStatement, databaseModel);

					var lastPreviousTerminal = lastStatement.GetNearestTerminalToPosition(cursorPosition);
					if (lastPreviousTerminal != null)
					{
						var completionItems = Enumerable.Empty<ICodeCompletionItem>();
						if (lastPreviousTerminal.Id == Terminals.From ||
						    lastPreviousTerminal.Id == Terminals.ObjectIdentifier)
						{
							var currentName = lastPreviousTerminal.Id == Terminals.From ? null : statementText.Substring(lastPreviousTerminal.SourcePosition.IndexStart, cursorPosition - lastPreviousTerminal.SourcePosition.IndexStart);
							completionItems = completionItems.Concat(GenerateSchemaObjectItems(databaseModel.CurrentSchema, currentName, lastPreviousTerminal));
						}

						if (lastPreviousTerminal.Id == Terminals.Dot &&
							lastPreviousTerminal.ParentNode.Id == NonTerminals.SchemaPrefix &&
							!lastPreviousTerminal.IsWithinSelectClauseOrCondition())
						{
							var ownerName = lastPreviousTerminal.ParentNode.ChildNodes.Single(n => n.Id == Terminals.SchemaIdentifier).Token.Value;
							completionItems = completionItems.Concat(GenerateSchemaObjectItems(ownerName, null, null));
						}

						if (lastPreviousTerminal.Id == Terminals.ObjectIdentifier ||
						    lastPreviousTerminal.Id == Terminals.Alias)
						{
							var joinClause = lastPreviousTerminal.GetPathFilterAncestor(n => n.Id != NonTerminals.FromClause, NonTerminals.JoinClause);
							if (joinClause != null)
							{
								var fromClause = joinClause.GetPathFilterAncestor(n => n.Id != NonTerminals.QueryBlock, NonTerminals.FromClause);
								if (fromClause != null)
								{
									var parentTableReference = fromClause.ChildNodes.Single(n => n.Id == NonTerminals.TableReference);
									var parentTable = parentTableReference.GetDescendantsWithinSameQuery(Terminals.ObjectIdentifier).SingleOrDefault();
									if (parentTable != null)
									{
										var queryBlock = semanticModel.GetQueryBlock(lastPreviousTerminal);
										var pTable = queryBlock.TableReferences.SingleOrDefault(t => t.Type == TableReferenceType.PhysicalObject && t.TableNode == parentTable);
										var cTable = queryBlock.TableReferences.SingleOrDefault(t => t.Type == TableReferenceType.PhysicalObject && (t.TableNode == lastPreviousTerminal || t.AliasNode == lastPreviousTerminal));

										var joinSuggestions = GenerateJoinConditionSuggestionItems(pTable, cTable);
										completionItems = completionItems.Concat(joinSuggestions);
									}
								}
							}
						}

						if (lastPreviousTerminal.Id == Terminals.ObjectIdentifier || lastPreviousTerminal.Id == Terminals.Alias)
						{
							var tableReference = lastPreviousTerminal.GetPathFilterAncestor(n => n.Id != NonTerminals.NestedQuery, NonTerminals.TableReference);
							if (tableReference != null && tableReference.ParentNode.Id == NonTerminals.FromClause && tableReference == tableReference.ParentNode.ChildNodes.First())
							{
								var alias = tableReference.GetDescendantsWithinSameQuery(Terminals.Alias).SingleOrDefault();
								if (alias == null || !String.Equals(alias.Token.Value, Terminals.Join, StringComparison.InvariantCultureIgnoreCase))
								{
									completionItems = completionItems.Concat(
										JoinClauses.Where(j => alias == null || j.Name.Contains(alias.Token.Value.ToUpperInvariant()))
											.Select(j => new OracleCodeCompletionItem
											             {
												             Name = j.Name,
															 Category = j.Category,
															 CategoryPriority = j.CategoryPriority,
															 Priority = j.Priority,
												             StatementNode = lastPreviousTerminal
											             }));
								}
							}
						}

						if (lastPreviousTerminal.Id == Terminals.Join)
						{
							completionItems = completionItems.Concat(GenerateSchemaObjectItems(databaseModel.CurrentSchema, null, null));
						}

						return completionItems.OrderBy(i => i.CategoryPriority).ThenBy(i => i.Priority).ThenBy(i => i.Name).ToArray();
					}
				}
				
				return EmptyCollection;
			}

			var currentNode = statement.GetNodeAtPosition(cursorPosition);
			semanticModel = new OracleStatementSemanticModel(statementText, statement, databaseModel);

			if (currentNode.Id == Terminals.Identifier)
			{
				var selectList = currentNode.GetPathFilterAncestor(n => n.Id != NonTerminals.QueryBlock, NonTerminals.SelectList);
				var condition = currentNode.GetPathFilterAncestor(n => n.Id != NonTerminals.QueryBlock, NonTerminals.Condition);
				var rootNode = selectList ?? condition;
				if (selectList != null || condition != null)
				{
					var prefixedColumnReference = currentNode.GetPathFilterAncestor(n => n.Id != NonTerminals.Expression, NonTerminals.PrefixedColumnReference);
					if (prefixedColumnReference != null)
					{
						var objectIdentifier = prefixedColumnReference.GetSingleDescendant(Terminals.ObjectIdentifier);
						if (objectIdentifier != null)
						{
							var queryBlock = semanticModel.GetQueryBlock(rootNode);
							var columnReferences = queryBlock.Columns.SelectMany(c => c.ColumnReferences).Where(c => c.TableNode == objectIdentifier).ToArray();
							if (columnReferences.Length == 1 && columnReferences[0].TableNode != null)
							{
								if (columnReferences[0].TableNodeReferences.Count == 1)
								{
									var currentName = statementText.Substring(currentNode.SourcePosition.IndexStart, cursorPosition - currentNode.SourcePosition.IndexStart);
									return columnReferences[0].TableNodeReferences.Single().Columns
										.Where(c => String.IsNullOrEmpty(currentName) || c.Name.Contains(currentName.ToUpperInvariant()))
										.Select(c => new OracleCodeCompletionItem
										             {
											             Name = c.Name.ToSimpleIdentifier(),
														 StatementNode = currentNode,
														 Category = "Column"
										             }).ToArray();
								}
							}
						}
					}
				}
			}

			if (currentNode.Id == Terminals.ObjectIdentifier &&
				!currentNode.IsWithinSelectClauseOrCondition())
			{
				// TODO: Add option to search all/current/public schemas
				var schemaIdentifier = currentNode.ParentNode.GetSingleDescendant(Terminals.SchemaIdentifier);

				var schemaName = schemaIdentifier != null
					? schemaIdentifier.Token.Value
					: databaseModel.CurrentSchema;

				var currentName = statementText.Substring(currentNode.SourcePosition.IndexStart, cursorPosition - currentNode.SourcePosition.IndexStart);
				return GenerateSchemaObjectItems(schemaName, currentName, currentNode).OrderBy(i => i.CategoryPriority).ThenBy(i => i.Priority).ThenBy(i => i.Name).ToArray();
			}

			return EmptyCollection;
		}

		private IEnumerable<ICodeCompletionItem> GenerateSchemaObjectItems(string schemaName, string objectNamePart, StatementDescriptionNode node)
		{
			return DatabaseModelFake.Instance.AllObjects.Values
						.Where(o => o.Owner == schemaName.ToQuotedIdentifier() && (String.IsNullOrEmpty(objectNamePart) || o.Name.Contains(objectNamePart.ToUpperInvariant())))
						.Select(o => new OracleCodeCompletionItem
						{
							Name = o.Name.ToSimpleIdentifier(),
							StatementNode = node,
							Category = "Schema Object"
						});
		}

		private IEnumerable<ICodeCompletionItem> GenerateJoinConditionSuggestionItems(OracleTableReference parentTable, OracleTableReference joinedTable)
		{
			if (parentTable.Type != TableReferenceType.PhysicalObject || parentTable.SearchResult.SchemaObject == null ||
				joinedTable.Type != TableReferenceType.PhysicalObject || joinedTable.SearchResult.SchemaObject == null)
				return EmptyCollection;

			var parentObject = parentTable.SearchResult.SchemaObject;
			var joinedObject = joinedTable.SearchResult.SchemaObject;

			var joinedToParentKeys = parentObject.ForeignKeys.Where(k => k.TargetObject == joinedObject.FullyQualifiedName)
				.Select(k => GenerateJoinConditionSuggestionItem(parentTable.FullyQualifiedName, joinedTable.FullyQualifiedName, k, false));

			var parentToJoinedKeys = joinedObject.ForeignKeys.Where(k => k.TargetObject == parentObject.FullyQualifiedName)
				.Select(k => GenerateJoinConditionSuggestionItem(joinedTable.FullyQualifiedName, parentTable.FullyQualifiedName, k, true));

			// TODO: Add suggestion based on column name

			return joinedToParentKeys.Concat(parentToJoinedKeys);
		}

		private OracleCodeCompletionItem GenerateJoinConditionSuggestionItem(OracleObjectIdentifier sourceObject, OracleObjectIdentifier targetObject, OracleForeignKeyConstraint foreignKey, bool swapSides)
		{
			var builder = new StringBuilder("ON ");
			var op = String.Empty;

			for (var i = 0; i < foreignKey.SourceColumns.Count; i++)
			{
				builder.Append(op);
				builder.Append(swapSides ? targetObject : sourceObject);
				builder.Append('.');
				builder.Append((swapSides ? foreignKey.TargetColumns[i] : foreignKey.SourceColumns[i]).ToSimpleIdentifier());
				builder.Append(" = ");
				builder.Append(swapSides ? sourceObject : targetObject);
				builder.Append('.');
				builder.Append((swapSides ? foreignKey.SourceColumns[i] : foreignKey.TargetColumns[i]).ToSimpleIdentifier());

				op = " AND ";
			}

			return new OracleCodeCompletionItem { Name = builder.ToString() };
		}
	}

	[DebuggerDisplay("OracleCodeCompletionItem (Name={Name}; Category={Category}; Priority={Priority})")]
	public class OracleCodeCompletionItem : ICodeCompletionItem
	{
		public string Category { get; set; }
		
		public string Name { get; set; }
		
		public StatementDescriptionNode StatementNode { get; set; }

		public int Priority { get; set; }

		public int CategoryPriority { get; set; }
	}
}
