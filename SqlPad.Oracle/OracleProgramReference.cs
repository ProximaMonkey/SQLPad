using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SqlPad.Oracle
{
	[DebuggerDisplay("OracleProgramReference (Owner={OwnerNode == null ? null : OwnerNode.Token.Value}; Object={ObjectNode == null ? null : ObjectNode.Token.Value}; Function={FunctionIdentifierNode.Token.Value})")]
	public class OracleProgramReference : OracleProgramReferenceBase
	{
		public override string Name { get { return FunctionIdentifierNode.Token.Value; } }

		public StatementGrammarNode FunctionIdentifierNode { get; set; }
		
		public StatementGrammarNode AnalyticClauseNode { get; set; }
		
		public override OracleProgramMetadata Metadata { get; set; }
	}

	[DebuggerDisplay("OracleTypeReference (Owner={OwnerNode == null ? null : OwnerNode.Token.Value}; Type={ObjectNode.Token.Value})")]
	public class OracleTypeReference : OracleProgramReferenceBase
	{
		public override string Name { get { return ObjectNode.Token.Value; } }

		public override OracleProgramMetadata Metadata
		{
			get { return ((OracleTypeBase)SchemaObject.GetTargetSchemaObject()).GetConstructorMetadata(); }
			set { throw new NotSupportedException("Metadata cannot be set. It is inferred from type attributes"); }
		}
	}

	[DebuggerDisplay("OracleSequenceReference (Owner={OwnerNode == null ? null : OwnerNode.Token.Value}; Sequence={ObjectNode.Token.Value})")]
	public class OracleSequenceReference : OracleObjectWithColumnsReference
	{
		public override string Name { get { return ObjectNode.Token.Value; } }

		public override ICollection<OracleColumn> Columns
		{
			get { return ((OracleSequence)SchemaObject).Columns; }
		}

		public override ReferenceType Type
		{
			get { return ReferenceType.SchemaObject; }
		}
	}

	[DebuggerDisplay("OracleTableCollectionReference (Owner={OwnerNode == null ? null : OwnerNode.Token.Value}; ObjectIdentifier={ObjectNode.Token.Value})")]
	public class OracleTableCollectionReference : OracleDataObjectReference
	{
		private List<OracleColumn> _columns;
		
		public OracleProgramReference PipelinedFunctionReference { get; private set; }

		public OracleTableCollectionReference(OracleProgramReference pipelinedFunctionReference) : base(ReferenceType.TableCollection)
		{
			PipelinedFunctionReference = pipelinedFunctionReference;
		}

		public OracleProgramMetadata ProgramMetadata { get { return PipelinedFunctionReference.Metadata; } }

		public override string Name { get { return AliasNode == null ? null : AliasNode.Token.Value; } }

		public override OracleObjectIdentifier FullyQualifiedObjectName
		{
			get { return OracleObjectIdentifier.Create(null, Name); }
		}

		public override ICollection<OracleColumn> Columns
		{
			get { return _columns ?? BuildColumns(); }
		}

		private ICollection<OracleColumn> BuildColumns()
		{
			_columns = new List<OracleColumn>();

			var schemaObject = SchemaObject.GetTargetSchemaObject();
			var collectionType = schemaObject as OracleTypeCollection;
			if (collectionType != null)
			{
				_columns.Add(BuildColumnValueColumn(collectionType.ElementDataType));
			}
			else if (ProgramMetadata != null && ProgramMetadata.Parameters.Count > 1 &&
			         (ProgramMetadata.Parameters[0].DataType == "TABLE" || ProgramMetadata.Parameters[0].DataType == "VARRAY"))
			{
				var returnParameter = ProgramMetadata.Parameters.SingleOrDefault(p => p.Direction == ParameterDirection.ReturnValue && p.DataLevel == 1 && p.Position == 1);
				if (returnParameter != null)
				{
					if (returnParameter.DataType == OracleTypeBase.TypeCodeObject)
					{
						if (Owner.SemanticModel.DatabaseModel.AllObjects.TryGetValue(returnParameter.CustomDataType, out schemaObject))
						{
							var columns = ((OracleTypeObject)schemaObject).Attributes
								.Select(a =>
									new OracleColumn
									{
										DataType = a.DataType,
										Nullable = true,
										Name = a.Name
									});

							_columns.AddRange(columns);
						}
					}
					else if (Owner.SemanticModel.DatabaseModel.AllObjects.TryGetValue(ProgramMetadata.Parameters[0].CustomDataType, out schemaObject))
					{
						_columns.Add(BuildColumnValueColumn(((OracleTypeCollection)schemaObject).ElementDataType));
					}
				}
			}

			return _columns.AsReadOnly();
		}

		private static OracleColumn BuildColumnValueColumn(OracleDataType columnType)
		{
			var column =
				new OracleColumn
				{
					Name = "\"COLUMN_VALUE\"",
					DataType = columnType,
					Nullable = true
				};
			return column;
		}
	}

	[DebuggerDisplay("OracleSpecialTableReference (Alias={Name})")]
	public class OracleSpecialTableReference : OracleDataObjectReference
	{
		private readonly ICollection<OracleColumn> _columns;

		public OracleSpecialTableReference(ReferenceType referenceType, IEnumerable<OracleColumn> columns)
			: base(referenceType)
		{
			_columns = new List<OracleColumn>(columns).AsReadOnly();
		}

		public override string Name { get { return AliasNode == null ? null : AliasNode.Token.Value; } }

		public override OracleObjectIdentifier FullyQualifiedObjectName
		{
			get { return OracleObjectIdentifier.Create(null, Name); }
		}

		public override ICollection<OracleColumn> Columns
		{
			get { return _columns; }
		}
	}

	[DebuggerDisplay("OracleSqlModelReference (Columns={Columns.Count})")]
	public class OracleSqlModelReference : OracleDataObjectReference
	{
		private readonly IReadOnlyList<OracleSelectListColumn> _sqlModelColumns;
		private ICollection<OracleColumn> _columns;
		private readonly List<OracleReferenceContainer> _childContainers = new List<OracleReferenceContainer>();

		public OracleReferenceContainer SourceReferenceContainer { get; private set; }

		public OracleReferenceContainer DimensionReferenceContainer { get; private set; }
		
		public OracleReferenceContainer MeasuresReferenceContainer { get; private set; }

		public IReadOnlyCollection<OracleReferenceContainer> ChildContainers
		{
			get { return _childContainers.AsReadOnly(); }
		}

		public OracleSqlModelReference(OracleStatementSemanticModel semanticModel, IReadOnlyList<OracleSelectListColumn> columns, IEnumerable<OracleDataObjectReference> sourceReferences)
			: base(ReferenceType.SqlModel)
		{
			_sqlModelColumns = columns;
			
			SourceReferenceContainer = new OracleReferenceContainer(semanticModel);
			foreach (var column in columns)
			{
				SourceReferenceContainer.ColumnReferences.AddRange(column.ColumnReferences);
				SourceReferenceContainer.ProgramReferences.AddRange(column.ProgramReferences);
				SourceReferenceContainer.TypeReferences.AddRange(column.TypeReferences);
			}
			
			SourceReferenceContainer.ObjectReferences.AddRange(sourceReferences);

			DimensionReferenceContainer = new OracleReferenceContainer(semanticModel);
			MeasuresReferenceContainer = new OracleReferenceContainer(semanticModel);

			_childContainers.Add(SourceReferenceContainer);
			_childContainers.Add(DimensionReferenceContainer);
			_childContainers.Add(MeasuresReferenceContainer);
		}

		public override ICollection<OracleColumn> Columns
		{
			get { return _columns ?? (_columns = _sqlModelColumns.Select(c => c.ColumnDescription).ToArray()); }
		}
	}

	public abstract class OracleProgramReferenceBase : OracleReference
	{
		public StatementGrammarNode ParameterListNode { get; set; }

		public IList<StatementGrammarNode> ParameterNodes { get; set; }

		public abstract OracleProgramMetadata Metadata { get; set; }
	}
}
