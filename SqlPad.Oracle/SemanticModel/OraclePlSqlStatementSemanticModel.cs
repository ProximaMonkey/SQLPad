﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SqlPad.Oracle.DatabaseConnection;
using SqlPad.Oracle.DataDictionary;
using NonTerminals = SqlPad.Oracle.OracleGrammarDescription.NonTerminals;
using Terminals = SqlPad.Oracle.OracleGrammarDescription.Terminals;

namespace SqlPad.Oracle.SemanticModel
{
	public class OraclePlSqlStatementSemanticModel : OracleStatementSemanticModel
	{
		private readonly List<OraclePlSqlProgram> _programs = new List<OraclePlSqlProgram>();

		public IReadOnlyList<OraclePlSqlProgram> Programs => _programs.AsReadOnly();

		public override OracleQueryBlock MainQueryBlock { get; } = null;

		public override IEnumerable<OracleReferenceContainer> AllReferenceContainers
		{
			get
			{
				return Programs
					.SelectMany(p => p.ChildModels)
					.SelectMany(m => m.AllReferenceContainers)
					.Concat(Programs);
			}
		}

		internal OraclePlSqlStatementSemanticModel(string statementText, OracleStatement statement, OracleDatabaseModelBase databaseModel)
			: base(statementText, statement, databaseModel)
		{
			if (!statement.IsPlSql)
			{
				throw new ArgumentException("Statement is not PL/SQL statement. ", nameof(statement));
			}
		}
		internal new OraclePlSqlStatementSemanticModel Build(CancellationToken cancellationToken)
		{
			return (OraclePlSqlStatementSemanticModel)base.Build(cancellationToken);
		}

		protected override void Build()
		{
			ResolveProgramDefinitions();
			ResolveProgramBodies();
		}

		private void ResolveProgramBodies()
		{
			foreach (var program in Programs)
			{
				foreach (var statementTypeNode in program.RootNode.GetPathFilterDescendants(n => !String.Equals(n.Id, NonTerminals.PlSqlSqlStatement) && !String.Equals(n.Id, NonTerminals.PlSqlStatementList) && (!String.Equals(n.Id, NonTerminals.PlSqlBlock) || !String.Equals(n.ParentNode.Id, NonTerminals.PlSqlStatementType)), NonTerminals.PlSqlStatementType))
				{
					var statementNode = statementTypeNode[0];
					if (statementNode == null)
					{
						continue;
					}

					FindPlSqlReferences(program, statementNode);
				}
			}

			ResolveSubProgramReferences(Programs);
		}

		private void FindPlSqlReferences(OraclePlSqlProgram program, StatementGrammarNode node)
		{
			if (node == null)
			{
				return;
			}

			var identifiers = node.GetPathFilterDescendants(NodeFilters.BreakAtPlSqlSubProgramOrSqlCommand, Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.Level, Terminals.RowNumberPseudoColumn);
			ResolveColumnFunctionOrDataTypeReferencesFromIdentifiers(null, program, identifiers, StatementPlacement.None, null, null, GetFunctionCallNodes);

			var grammarSpecificFunctions = GetGrammarSpecificFunctionNodes(node);
			CreateGrammarSpecificFunctionReferences(grammarSpecificFunctions, null, program.ProgramReferences, StatementPlacement.None, null);

			var assignmentTargetIdentifiers = node
				.GetPathFilterDescendants(NodeFilters.BreakAtPlSqlSubProgramOrSqlCommand, NonTerminals.BindVariableExpressionOrPlSqlTarget)
				.SelectMany(t => t.GetDescendants(Terminals.PlSqlIdentifier));

			ResolveColumnFunctionOrDataTypeReferencesFromIdentifiers(null, program, assignmentTargetIdentifiers, StatementPlacement.None, null);
		}

		private static IEnumerable<StatementGrammarNode> GetFunctionCallNodes(StatementGrammarNode identifier)
		{
			var parenthesisEnclosedFunctionParametersNode = identifier.ParentNode.ParentNode[NonTerminals.ParenthesisEnclosedFunctionParameters];
			if (parenthesisEnclosedFunctionParametersNode != null)
			{
				yield return parenthesisEnclosedFunctionParametersNode;
			}
		}

		private void ResolvePlSqlReferences(OraclePlSqlProgram program)
		{
			var sourceColumnReferences = program.ColumnReferences
				.Concat(
					program.ChildModels
						.SelectMany(m => m.AllReferenceContainers)
						.SelectMany(c => c.ColumnReferences)
						.Where(c => !c.ReferencesAllColumns && c.ColumnNodeColumnReferences.Count == 0 && c.ObjectNodeObjectReferences.Count == 0))
				.ToArray();

			foreach (var columnReference in sourceColumnReferences)
			{
				var variableReference =
					new OraclePlSqlVariableReference
					{
						PlSqlProgram = program,
						IdentifierNode = columnReference.ColumnNode
					};

				variableReference.CopyPropertiesFrom(columnReference);

				var variableResolved = TryResolveLocalVariableReference(variableReference);
				if (variableResolved)
				{
					columnReference.Container.ColumnReferences.Remove(columnReference);
				}
				else
				{
					variableResolved = !TryColumnReferenceAsProgramOrSequenceReference(columnReference, true);
				}

				if (variableResolved)
				{
					program.PlSqlVariableReferences.Add(variableReference);
				}
			}

			program.ColumnReferences.Clear();

			ResolveFunctionReferences(program.ProgramReferences, true);

			ResolveSubProgramReferences(program.SubPrograms);
		}

		private bool TryResolveLocalVariableReference(OraclePlSqlVariableReference variableReference)
		{
			if (variableReference.OwnerNode != null)
			{
				return false;
			}

			var variablePrefix = variableReference.ObjectNode == null ? null : variableReference.FullyQualifiedObjectName.NormalizedName;
			var program = variableReference.PlSqlProgram;

			do
			{
				if (variablePrefix == null || GetScopeNames(program).Any(n => String.Equals(variablePrefix, n)))
				{
					foreach (var variable in ((IEnumerable<OraclePlSqlElement>)variableReference.PlSqlProgram.Variables).Concat(variableReference.PlSqlProgram.Parameters))
					{
						if (String.Equals(variable.Name, variableReference.NormalizedName))
						{
							variableReference.Variables.Add(variable);
						}
					}

					if (variableReference.Variables.Count > 0)
					{
						return true;
					}
				}

				program = program.Owner;
			} while (variableReference.Variables.Count == 0 && program != null);


			return false;
		}

		private static IEnumerable<string> GetScopeNames(OraclePlSqlProgram program)
		{
			return String.IsNullOrEmpty(program.Name)
				? program.Labels.Select(l => l.Name)
				: Enumerable.Repeat(program.Name, 1);
		}

		private void ResolveSubProgramReferences(IEnumerable<OraclePlSqlProgram> programs)
		{
			foreach (var subProgram in programs)
			{
				ResolvePlSqlReferences(subProgram);
			}
		}

		private void ResolveProgramDefinitions()
		{
			var identifier = OracleObjectIdentifier.Empty;

			var anonymousPlSqlBlock = String.Equals(Statement.RootNode.Id, NonTerminals.PlSqlBlockStatement);
			var functionOrProcedure = Statement.RootNode[NonTerminals.CreatePlSqlObjectClause]?[0];
			var isSchemaProcedure = String.Equals(functionOrProcedure?.Id, NonTerminals.CreateProcedure);
			var isSchemaFunction = String.Equals(functionOrProcedure?.Id, NonTerminals.CreateFunction);
			if (isSchemaProcedure || isSchemaFunction || anonymousPlSqlBlock)
			{
				StatementGrammarNode schemaObjectNode = null;
				if (isSchemaFunction)
				{
					schemaObjectNode = functionOrProcedure[NonTerminals.PlSqlFunctionSource, NonTerminals.SchemaObject];
				}
				else if (isSchemaProcedure)
				{
					schemaObjectNode = functionOrProcedure[NonTerminals.SchemaObject];
				}
				else
				{
					functionOrProcedure = Statement.RootNode[NonTerminals.PlSqlBlock];
				}

				if (schemaObjectNode != null)
				{
					var owner = schemaObjectNode[NonTerminals.SchemaPrefix, Terminals.SchemaIdentifier]?.Token.Value ?? DatabaseModel.CurrentSchema;
					var name = schemaObjectNode[Terminals.ObjectIdentifier]?.Token.Value;
					identifier = OracleObjectIdentifier.Create(owner, name);
				}

				var program =
					new OraclePlSqlProgram(anonymousPlSqlBlock ? PlSqlProgramType.PlSqlBlock : PlSqlProgramType.StandaloneProgram, this)
					{
						RootNode = functionOrProcedure,
						ObjectIdentifier = identifier,
						Name = identifier.NormalizedName
					};

				ResolveParameterDeclarations(program);
				ResolveLocalVariableTypeAndLabelDeclarations(program);
				ResolveSqlStatements(program);

				_programs.Add(program);

				ResolveSubProgramDefinitions(program);
			}
			else
			{
				// TODO: packages
				//var programDefinitionNodes = Statement.RootNode.GetDescendants(NonTerminals.FunctionDefinition, NonTerminals.ProcedureDefinition);
				//_programs.AddRange(programDefinitionNodes.Select(n => new OraclePlSqlProgram { RootNode = n }));
			}
		}

		private void ResolveSqlStatements(OraclePlSqlProgram program)
		{
			var sqlStatementNodes = program.RootNode.GetPathFilterDescendants(
				n => !String.Equals(n.Id, NonTerminals.ItemList2) && (!String.Equals(n.Id, NonTerminals.PlSqlBlock) || !String.Equals(n.ParentNode.Id, NonTerminals.PlSqlStatementType)),
				NonTerminals.SelectStatement, NonTerminals.InsertStatement, NonTerminals.UpdateStatement, NonTerminals.MergeStatement);

			foreach (var sqlStatementNode in sqlStatementNodes)
			{
				var childStatement = new OracleStatement { RootNode = sqlStatementNode, ParseStatus = Statement.ParseStatus, SourcePosition = sqlStatementNode.SourcePosition };
				var childStatementSemanticModel = new OracleStatementSemanticModel(sqlStatementNode.GetText(StatementText), childStatement, DatabaseModel).Build(CancellationToken);
				program.ChildModels.Add(childStatementSemanticModel);

				foreach (var queryBlock in childStatementSemanticModel.QueryBlocks)
				{
					QueryBlockNodes.Add(queryBlock.RootNode, queryBlock);
				}
			}
		}

		private void ResolveSubProgramDefinitions(OraclePlSqlProgram program)
		{
			foreach (var childNode in program.RootNode.ChildNodes)
			{
				ResolveSubProgramDefinitions(program, childNode);
			}
		}

		private void ResolveSubProgramDefinitions(OraclePlSqlProgram program, StatementGrammarNode node)
		{
			foreach (var childNode in node.ChildNodes)
			{
				var subProgram = program;
				var isPlSqlBlock = String.Equals(childNode.Id, NonTerminals.PlSqlBlock);
				if (isPlSqlBlock || String.Equals(childNode.Id, NonTerminals.ProcedureDefinition) || String.Equals(childNode.Id, NonTerminals.FunctionDefinition))
				{
					var nameTerminal = childNode[0]?[Terminals.Identifier];

					subProgram =
						new OraclePlSqlProgram(isPlSqlBlock ? PlSqlProgramType.PlSqlBlock : PlSqlProgramType.NestedProgram, this)
						{
							Owner = program,
							RootNode = childNode,
							ObjectIdentifier = program.ObjectIdentifier
						};

					ResolveParameterDeclarations(subProgram);
					ResolveLocalVariableTypeAndLabelDeclarations(subProgram);
					ResolveSqlStatements(subProgram);

					if (nameTerminal != null)
					{
						subProgram.Name = nameTerminal.Token.Value.ToQuotedIdentifier();
					}

					program.SubPrograms.Add(subProgram);
				}

				ResolveSubProgramDefinitions(subProgram, childNode);
			}
		}

		private void ResolveLocalVariableTypeAndLabelDeclarations(OraclePlSqlProgram program)
		{
			StatementGrammarNode programSourceNode;
			switch (program.RootNode.Id)
			{
				case NonTerminals.CreateFunction:
					programSourceNode = program.RootNode[NonTerminals.PlSqlFunctionSource, NonTerminals.FunctionTypeDefinition, NonTerminals.ProgramImplentationDeclaration];
					break;
				case NonTerminals.PlSqlBlock:
					programSourceNode = program.RootNode[NonTerminals.PlSqlBlockDeclareSection];
					break;
				default:
					programSourceNode = program.RootNode[NonTerminals.ProgramImplentationDeclaration];
					break;
			}

			ResolveProgramDeclarationLabels(program);

			var item1 = programSourceNode?[NonTerminals.ProgramDeclareSection, NonTerminals.ItemList1, NonTerminals.Item1];
			if (item1 == null)
			{
				return;
			}

			var itemDeclarations = StatementGrammarNode.GetAllChainedClausesByPath(item1, n => String.Equals(n.ParentNode.Id, NonTerminals.Item1OrPragmaDefinition) ? n.ParentNode.ParentNode : n.ParentNode, NonTerminals.ItemList1Chained, NonTerminals.Item1OrPragmaDefinition, NonTerminals.Item1).ToArray();
			foreach (var itemDeclaration in itemDeclarations)
			{
				var declarationRoot = itemDeclaration[0];
				switch (declarationRoot?.Id)
				{
					case NonTerminals.ItemDeclaration:
						var specificNode = declarationRoot[0];
						if (specificNode != null)
						{
							OraclePlSqlVariable variable;
							switch (specificNode.Id)
							{
								case NonTerminals.ConstantDeclaration:
									variable =
										new OraclePlSqlVariable
										{
											IsConstant = true,
											DefaultExpression = specificNode[NonTerminals.PlSqlExpression]
										};

									SetDataTypeAndNullablePropertiesAndAddIfIdentifierFound(variable, specificNode, program);
									break;

								case NonTerminals.ExceptionDeclaration:
									var exceptionName = specificNode[Terminals.ExceptionIdentifier]?.Token.Value.ToQuotedIdentifier();
									if (exceptionName != null)
									{
										var exception = new OraclePlSqlException { Name = exceptionName };
										program.Exceptions.Add(exception);
									}

									break;

								case NonTerminals.VariableDeclaration:
									specificNode = specificNode[NonTerminals.FieldDefinition];
									if (specificNode != null)
									{
										variable =
											new OraclePlSqlVariable
											{
												DefaultExpression = specificNode[NonTerminals.VariableDeclarationDefaultValue, NonTerminals.PlSqlExpression],
											};

										SetDataTypeAndNullablePropertiesAndAddIfIdentifierFound(variable, specificNode, program);
									}

									break;
							}
						}

						break;

					case NonTerminals.TypeDefinition:
						var typeIdentifierNode = declarationRoot[Terminals.TypeIdentifier];
						if (typeIdentifierNode != null)
						{
							program.Types.Add(new OraclePlSqlType { Name = typeIdentifierNode.Token.Value.ToQuotedIdentifier() });
						}

						break;
				}
			}
		}

		private static void SetDataTypeAndNullablePropertiesAndAddIfIdentifierFound(OraclePlSqlVariable variable, StatementGrammarNode variableNode, OraclePlSqlProgram program)
		{
			var identifierNode = variableNode[Terminals.Identifier];
			if (identifierNode == null)
			{
				return;
			}

			variable.Name = identifierNode.Token.Value.ToQuotedIdentifier();
			variable.Nullable = variableNode[NonTerminals.NotNull, Terminals.Not] == null;
			variable.DataTypeNode = variableNode[NonTerminals.PlSqlDataType];
			program.Variables.Add(variable);
		}

		private static void ResolveProgramDeclarationLabels(OraclePlSqlProgram program)
		{
			var labelListNode = program.RootNode[NonTerminals.PlSqlLabelList];
			if (String.Equals(program.RootNode.ParentNode.Id, NonTerminals.PlSqlStatementType))
			{
				labelListNode = program.RootNode.ParentNode.ParentNode[NonTerminals.PlSqlLabelList];
			}

			if (labelListNode != null)
			{
				foreach (var labelIdentifier in labelListNode.GetDescendants(Terminals.LabelIdentifier))
				{
					var label =
						new OraclePlSqlLabel
						{
							Name = labelIdentifier.Token.Value.ToQuotedIdentifier(),
							Node = labelIdentifier.ParentNode
						};

					program.Labels.Add(label);
				}
			}
		}

		private void ResolveParameterDeclarations(OraclePlSqlProgram program)
		{
			StatementGrammarNode parameterSourceNode;
			switch (program.RootNode.Id)
			{
				case NonTerminals.CreateFunction:
					parameterSourceNode = program.RootNode[NonTerminals.PlSqlFunctionSource];
					break;
				case NonTerminals.CreateProcedure:
					parameterSourceNode = program.RootNode;
					break;
				default:
					parameterSourceNode = program.RootNode[0];
					break;
			}

			var parameterDeclarationList = parameterSourceNode?[NonTerminals.ParenthesisEnclosedParameterDeclarationList, NonTerminals.ParameterDeclarationList];
			if (parameterDeclarationList == null)
			{
				return;
			}

			var parameterDeclarations = StatementGrammarNode.GetAllChainedClausesByPath(parameterDeclarationList, null, NonTerminals.ParameterDeclarationListChained, NonTerminals.ParameterDeclarationList)
				.Select(n => n[NonTerminals.ParameterDeclaration]);
			foreach (var parameterDeclaration in parameterDeclarations)
			{
				program.Parameters.Add(ResolveParameter(parameterDeclaration));
			}

			var returnParameterNode = parameterSourceNode[NonTerminals.PlSqlDataTypeWithoutConstraint];
			if (returnParameterNode != null)
			{
				program.ReturnParameter =
					new OraclePlSqlParameter
					{
						Direction = ParameterDirection.ReturnValue,
						Nullable = true,
						DataTypeNode = returnParameterNode
					};
			}
		}

		private static OraclePlSqlParameter ResolveParameter(StatementGrammarNode parameterDeclaration)
		{
			var parameter =
				new OraclePlSqlParameter
				{
					Name = parameterDeclaration[Terminals.ParameterIdentifier].Token.Value.ToQuotedIdentifier()
				};

			var direction = ParameterDirection.Input;
			var parameterDirectionDeclaration = parameterDeclaration[NonTerminals.ParameterDirectionDeclaration];
			if (parameterDirectionDeclaration != null)
			{
				if (parameterDirectionDeclaration[Terminals.Out] != null)
				{
					direction = parameterDeclaration[Terminals.In] == null ? ParameterDirection.Output : ParameterDirection.InputOutput;
				}

				parameter.DataTypeNode = parameterDirectionDeclaration[NonTerminals.PlSqlDataTypeWithoutConstraint];
				parameter.DefaultExpression = parameterDirectionDeclaration[NonTerminals.VariableDeclarationDefaultValue];
			}

			parameter.Direction = direction;

			return parameter;
		}
	}

	public enum PlSqlProgramType
	{
		PlSqlBlock,
		StandaloneProgram,
		PackageProgram,
		NestedProgram
	}

	[DebuggerDisplay("OraclePlSqlProgram (Name={Name}; ObjectOwner={ObjectIdentifier.Owner}; ObjectName={ObjectIdentifier.Name})")]
	public class OraclePlSqlProgram : OracleReferenceContainer
	{
		public OraclePlSqlProgram(PlSqlProgramType type, OraclePlSqlStatementSemanticModel semanticModel) : base(semanticModel)
		{
			Type = type;
		}

		public PlSqlProgramType Type { get; }

		public StatementGrammarNode RootNode { get; set; }

		public OracleObjectIdentifier ObjectIdentifier { get; set; }

		public string Name { get; set; }

		public IList<OracleStatementSemanticModel> ChildModels { get; } = new List<OracleStatementSemanticModel>();

		public IList<OraclePlSqlProgram> SubPrograms { get; } = new List<OraclePlSqlProgram>();

		public OraclePlSqlProgram Owner { get; set; }

		public IList<OraclePlSqlParameter> Parameters { get; } = new List<OraclePlSqlParameter>();

		public IList<OraclePlSqlType> Types { get; } = new List<OraclePlSqlType>();

		public IList<OraclePlSqlVariable> Variables { get; } = new List<OraclePlSqlVariable>();

		public IList<OraclePlSqlException> Exceptions { get; } = new List<OraclePlSqlException>();

		public IList<OraclePlSqlLabel> Labels { get; } = new List<OraclePlSqlLabel>();

		public OraclePlSqlParameter ReturnParameter { get; set; }
	}

	[DebuggerDisplay("OraclePlSqlVariableReference (Name={Name}; Variables={Variables.Count})")]
	public class OraclePlSqlVariableReference : OracleReference
	{
		public StatementGrammarNode IdentifierNode { get; set; }

		public OraclePlSqlProgram PlSqlProgram { get; set; }

		public override string Name => IdentifierNode.Token.Value;

		public ICollection<OraclePlSqlElement> Variables { get; } = new List<OraclePlSqlElement>();

		public override void Accept(IOracleReferenceVisitor visitor)
		{
			visitor.VisitPlSqlVariableReference(this);
		}
	}

	public abstract class OraclePlSqlElement
	{
		public string Name { get; set; }
	}

	public class OraclePlSqlException : OraclePlSqlElement
	{
		
	}

	public class OraclePlSqlVariable : OraclePlSqlElement
	{
		public bool IsConstant { get; set; }

		public bool Nullable { get; set; }

		public StatementGrammarNode DefaultExpression { get; set; }

		public StatementGrammarNode DataTypeNode { get; set; }
	}

	public class OraclePlSqlParameter : OraclePlSqlVariable
	{
		public ParameterDirection Direction { get; set; }
	}

	public class OraclePlSqlType : OraclePlSqlElement
	{
	}

	public class OraclePlSqlLabel : OraclePlSqlElement
	{
		public StatementGrammarNode Node { get; set; }
	}
}
