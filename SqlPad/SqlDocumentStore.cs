﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SqlPad
{
	public class SqlDocumentStore
	{
		private readonly object _lockObject = new object();
		private IDictionary<StatementBase, IValidationModel> _validationModels = new Dictionary<StatementBase, IValidationModel>();
		private readonly ISqlParser _parser;
		private readonly IStatementValidator _validator;
		private readonly IDatabaseModel _databaseModel;
		private StatementCollection _statementCollection = new StatementCollection(new StatementBase[0]);

		public StatementCollection StatementCollection
		{
			get { return _statementCollection; }
		}

		public string StatementText { get; private set; }

		public IDictionary<StatementBase, IValidationModel> ValidationModels { get { return _validationModels; } }

		public SqlDocumentStore(ISqlParser parser, IStatementValidator validator, IDatabaseModel databaseModel, string statementText = null)
		{
			if (parser == null)
				throw new ArgumentNullException("parser");

			if (validator == null)
				throw new ArgumentNullException("validator");

			_parser = parser;
			_validator = validator;
			_databaseModel = databaseModel;

			if (!String.IsNullOrEmpty(statementText))
			{
				UpdateStatements(statementText);
			}
		}

		public void UpdateStatements(string statementText)
		{
			var statements = _parser.Parse(statementText);
			var validationModels = new ReadOnlyDictionary<StatementBase, IValidationModel>(statements.ToDictionary(s => s, s => _validator.BuildValidationModel(_validator.BuildSemanticModel(statementText, s, _databaseModel))));

			lock (_lockObject)
			{
				_statementCollection = statements;
				_validationModels = validationModels;
				StatementText = statementText;
			}
		}

		public void ExecuteStatementAction(Action<StatementCollection> action)
		{
			lock (_lockObject)
			{
				action(StatementCollection);
			}
		}

		public T ExecuteStatementAction<T>(Func<StatementCollection, T> function)
		{
			lock (_lockObject)
			{
				return function(StatementCollection);
			}
		}
	}
}