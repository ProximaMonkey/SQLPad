﻿using System.Configuration;
using SqlPad.Commands;

namespace SqlPad
{
	public interface IInfrastructureFactory
	{
		ICommandFactory CommandFactory { get; }

		ITokenReader CreateTokenReader(string sqlText);

		ISqlParser CreateSqlParser();

		IDatabaseModel CreateDatabaseModel(ConnectionStringSettings connectionString);

		IStatementValidator CreateStatementValidator();

		ICodeCompletionProvider CreateCodeCompletionProvider();

		ICodeSnippetProvider CreateSnippetProvider();

		IContextActionProvider CreateContextActionProvider();

		IMultiNodeEditorDataProvider CreateMultiNodeEditorDataProvider();

		IStatementFormatter CreateSqlFormatter(SqlFormatterOptions options);
	}

	public interface IMultiNodeEditorDataProvider
	{
		MultiNodeEditorData GetMultiNodeEditorData(IDatabaseModel databaseModel, string sqlText, int position, int selectionStart, int selectionLength);
	}
}