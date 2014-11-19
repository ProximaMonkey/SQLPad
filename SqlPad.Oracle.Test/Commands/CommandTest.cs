﻿using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.AvalonEdit;
using NUnit.Framework;
using Shouldly;
using SqlPad.Commands;
using SqlPad.Oracle.Commands;

namespace SqlPad.Oracle.Test.Commands
{
	[TestFixture]
	public class CommandTest
	{
		private SqlDocumentRepository _documentRepository;
		private TextEditor _editor;

		private const string FindUsagesStatementText =
@"SELECT
	NAME,
	TARGETGROUP_NAME,
	PNAME,
	RESPONDENTBUCKET_NAME,
	CONSTANT_COLUMN
FROM
	(SELECT
		NAME,
		TARGETGROUP_NAME || TARGETGROUP_NAME TARGETGROUP_NAME,
		PROJECT_NAME PNAME,
		RESPONDENTBUCKET_NAME,
		CONSTANT_COLUMN
	FROM
		(SELECT
			S.NAME,
			TG.NAME TARGETGROUP_NAME,
			P.NAME PROJECT_NAME,
			RB.NAME RESPONDENTBUCKET_NAME,
			'My column1' CONSTANT_COLUMN,
			'My column2'
		FROM
			HUSQVIK.SELECTION S
			LEFT JOIN RESPONDENTBUCKET RB ON S.RESPONDENTBUCKET_ID = RB.RESPONDENTBUCKET_ID
			LEFT JOIN TARGETGROUP TG ON RB.TARGETGROUP_ID = TG.TARGETGROUP_ID AND RB.NAME = TG.NAME
			JOIN PROJECT P ON S.PROJECT_ID = P.PROJECT_ID
		WHERE
			TG.NAME IN ('X1', 'X2') OR S.NAME IS NOT NULL OR P.NAME <> ''
		)
	) TMP
ORDER BY
	TMP.RESPONDENTBUCKET_NAME";

		private const string FindFunctionUsagesStatementText =
@"SELECT
	COUNT(*) OVER () CNT1,
	COUNT(1) OVER () CNT2,
	FIRST_VALUE(DUMMY) IGNORE NULLS OVER () FIRST_VAL1,
	FIRST_VALUE(DUMMY) OVER () FIRST_VAL2,
	COALESCE(DUMMY, 1) COALESCE1,
	COALESCE(DUMMY, 1) COALESCE2,
	TO_CHAR(0) C1,
	TO_CHAR(0) C2
FROM
	DUAL";

		private const string FindLiteralUsagesStatementText =
@"SELECT
	:BV,
	'123',
	'456',
	'123',
	'456',
	123,
	456,
	123,
	456,
	:BV
FROM
	SELECTION
WHERE
	'456' != '123'
	AND 1 = :BV";

		[SetUp]
		public void SetUp()
		{
			_editor = new TextEditor();
			_documentRepository = TestFixture.CreateDocumentRepository();
		}

		private class TestCommandSettings : ICommandSettingsProvider
		{
			private readonly bool _isValueValid;

			public TestCommandSettings(CommandSettingsModel settingsModel, bool isValueValid = true)
			{
				Settings = settingsModel;
				_isValueValid = isValueValid;
			}

			public EventHandler GetSettingsCalled;

			public bool GetSettings()
			{
				if (GetSettingsCalled != null)
				{
					GetSettingsCalled(this, EventArgs.Empty);
				}

				return _isValueValid;
			}

			public CommandSettingsModel Settings { get; private set; }
		}

		private CommandExecutionContext CreateExecutionContext()
		{
			_documentRepository.UpdateStatements(_editor.Text);
			return CommandExecutionContext.Create(_editor, _documentRepository);
		}

		private bool CanExecuteCommand(CommandExecutionHandler executionHandler)
		{
			var executionContext = CreateExecutionContext();
			return executionHandler.CanExecuteHandler(executionContext);
		}

		private void ExecuteCommand(CommandExecutionHandler executionHandler, ICommandSettingsProvider commandSettings = null)
		{
			var executionContext = CreateExecutionContext();
			executionContext.SettingsProvider = commandSettings;
			ExecuteCommand(executionHandler, executionContext);
		}

		private void ExecuteCommand(CommandExecutionHandler executionHandler, CommandExecutionContext executionContext)
		{
			executionHandler.ExecutionHandler(executionContext);
			GenericCommandHandler.UpdateDocument(_editor, executionContext);
		}

		[Test(Description = @""), STAThread]
		public void TestAddObjectAliasCommand()
		{
			_editor.Text = @"SELECT SELECTION.RESPONDENTBUCKET_ID, SELECTION.SELECTION_ID, PROJECT_ID, NAME FROM SELECTION";
			_editor.CaretOffset = 87;

			CanExecuteCommand(OracleCommands.AddAlias).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddAlias, new TestCommandSettings(new CommandSettingsModel { Value = "S"} ));

			_editor.Text.ShouldBe(@"SELECT S.RESPONDENTBUCKET_ID, S.SELECTION_ID, PROJECT_ID, NAME FROM SELECTION S");
		}

		[Test(Description = @""), STAThread]
		public void TestAddColumnAliasCommand()
		{
			_editor.Text = @"SELECT 'Prefix' || TBL.RESPONDENTBUCKET_ID || 'Postfix', NAME FROM (SELECT RESPONDENTBUCKET_ID, NAME FROM (SELECT RESPONDENTBUCKET_ID, NAME FROM SELECTION) WHERE RESPONDENTBUCKET_ID > 0) TBL";
			_editor.CaretOffset = 114;

			CanExecuteCommand(OracleCommands.AddAlias).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddAlias, new TestCommandSettings(new CommandSettingsModel { Value = "RBID"} ));

			_editor.Text.ShouldBe(@"SELECT 'Prefix' || TBL.RBID || 'Postfix', NAME FROM (SELECT RBID, NAME FROM (SELECT RESPONDENTBUCKET_ID RBID, NAME FROM SELECTION) WHERE RBID > 0) TBL");
		}

		[Test(Description = @""), STAThread]
		public void TestAddAliasCommandAtTableWithAlias()
		{
			_editor.Text = @"SELECT S.RESPONDENTBUCKET_ID, S.SELECTION_ID, PROJECT_ID, NAME FROM SELECTION S";
			_editor.CaretOffset = 70;

			CanExecuteCommand(OracleCommands.AddAlias).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestAddAliasCommandWithWhereGroupByAndHavingClauses()
		{
			_editor.Text = "SELECT SELECTION.RESPONDENTBUCKET_ID, PROJECT_ID FROM SELECTION WHERE SELECTION.NAME = NAME GROUP BY SELECTION.RESPONDENTBUCKET_ID, PROJECT_ID HAVING COUNT(SELECTION.SELECTION_ID) = COUNT(SELECTION_ID)";
			_editor.CaretOffset = 60;

			CanExecuteCommand(OracleCommands.AddAlias).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddAlias, new TestCommandSettings(new CommandSettingsModel { Value = "S" } ));

			_editor.Text.ShouldBe("SELECT S.RESPONDENTBUCKET_ID, PROJECT_ID FROM SELECTION S WHERE S.NAME = NAME GROUP BY S.RESPONDENTBUCKET_ID, PROJECT_ID HAVING COUNT(S.SELECTION_ID) = COUNT(SELECTION_ID)");
		}

		[Test(Description = @""), STAThread]
		public void TestBasicWrapAsInlineViewCommand()
		{
			_editor.Text = @"SELECT S.RESPONDENTBUCKET_ID, S.SELECTION_ID, PROJECT_ID, NAME, 1 FROM SELECTION S";
			_editor.CaretOffset = 0;

			ExecuteCommand(OracleCommands.WrapAsInlineView, new TestCommandSettings(new CommandSettingsModel { Value = "IV" } ));

			_editor.Text.ShouldBe(@"SELECT IV.RESPONDENTBUCKET_ID, IV.SELECTION_ID, IV.PROJECT_ID, IV.NAME FROM (SELECT S.RESPONDENTBUCKET_ID, S.SELECTION_ID, PROJECT_ID, NAME, 1 FROM SELECTION S) IV");
		}

		[Test(Description = @""), STAThread]
		public void TestBasicWrapAsInlineViewCommandWithFunctionInvokationWithSingleIdentifierParameter()
		{
			_editor.Text = @"SELECT COUNT(DISTINCT SELECTION_ID) OVER (), RESPONDENTBUCKET_ID FROM SELECTION";
			_editor.CaretOffset = 0;

			ExecuteCommand(OracleCommands.WrapAsInlineView, new TestCommandSettings(new CommandSettingsModel { Value = "IV" }));

			_editor.Text.ShouldBe(@"SELECT IV.RESPONDENTBUCKET_ID FROM (SELECT COUNT(DISTINCT SELECTION_ID) OVER (), RESPONDENTBUCKET_ID FROM SELECTION) IV");
		}

		[Test(Description = @""), STAThread]
		public void TestBasicWrapAsCommonTableExpressionCommand()
		{
			_editor.Text = "SELECT 1, 1 + 1 MYCOLUMN, DUMMY || '3' COLUMN3 FROM DUAL";
			_editor.CaretOffset = 0;

			ExecuteCommand(OracleCommands.WrapAsCommonTableExpression, new TestCommandSettings(new CommandSettingsModel { Value = "MYQUERY" } ));

			_editor.Text.ShouldBe(@"WITH MYQUERY AS (SELECT 1, 1 + 1 MYCOLUMN, DUMMY || '3' COLUMN3 FROM DUAL) SELECT MYQUERY.MYCOLUMN, MYQUERY.COLUMN3 FROM MYQUERY");
		}

		[Test(Description = @""), STAThread]
		public void TestBasicWrapAsCommonTableExpressionCommandWithExistingCommonTableExpressionAndWhiteSpace()
		{
			_editor.Text = "\t\t            WITH OLDQUERY AS (SELECT OLD FROM OLD) SELECT 1, 1 + 1 MYCOLUMN, DUMMY || '3' COLUMN3 FROM DUAL";
			_editor.CaretOffset = 55;

			ExecuteCommand(OracleCommands.WrapAsCommonTableExpression, new TestCommandSettings(new CommandSettingsModel { Value = "NEWQUERY" }));

			_editor.Text.ShouldBe("\t\t            WITH OLDQUERY AS (SELECT OLD FROM OLD), NEWQUERY AS (SELECT 1, 1 + 1 MYCOLUMN, DUMMY || '3' COLUMN3 FROM DUAL) SELECT NEWQUERY.MYCOLUMN, NEWQUERY.COLUMN3 FROM NEWQUERY");
		}

		[Test(Description = @""), STAThread]
		public void TestBasicToggleQuotedNotationCommandOn()
		{
			_editor.Text = "SELECT \"PUBLIC\".DUAL.DUMMY, S.PROJECT_ID FROM SELECTION S, \"PUBLIC\".DUAL";
			_editor.CaretOffset = 0;

			ExecuteCommand(OracleCommands.ToggleQuotedNotation);

			_editor.Text.ShouldBe("SELECT \"PUBLIC\".\"DUAL\".\"DUMMY\", \"S\".\"PROJECT_ID\" FROM \"SELECTION\" \"S\", \"PUBLIC\".\"DUAL\"");
		}

		[Test(Description = @""), STAThread]
		public void TestBasicToggleQuotedNotationCommandOff()
		{
			_editor.Text = "SELECT \"PUBLIC\".\"DUAL\".\"DUMMY\", \"S\".\"PROJECT_ID\" FROM \"SELECTION\" \"S\", \"PUBLIC\".\"DUAL\"";
			_editor.CaretOffset = 0;

			ExecuteCommand(OracleCommands.ToggleQuotedNotation);

			_editor.Text.ShouldBe("SELECT \"PUBLIC\".DUAL.DUMMY, S.PROJECT_ID FROM SELECTION S, \"PUBLIC\".DUAL");
		}

		[Test(Description = @""), STAThread]
		public void TestBasicToggleQuotedNotationCommandWithSubqueryWithQuotedNotation()
		{
			_editor.Text = "SELECT DUMMY FROM (SELECT \"DUMMY\" FROM \"DUAL\")";
			_editor.CaretOffset = 0;

			ExecuteCommand(OracleCommands.ToggleQuotedNotation);

			_editor.Text.ShouldBe("SELECT \"DUMMY\" FROM (SELECT \"DUMMY\" FROM \"DUAL\")");
		}

		private List<TextSegment> FindUsagesOrdered(string statementText, int currentPosition)
		{
			_documentRepository.UpdateStatements(statementText);
			var executionContext = new CommandExecutionContext(statementText, currentPosition, currentPosition, 0, _documentRepository);
			FindUsagesCommand.FindUsages.ExecutionHandler(executionContext);
			return executionContext.SegmentsToReplace.OrderBy(s => s.IndextStart).ToList();
		}
			
		[Test(Description = @""), STAThread]
		public void TestFindObjectUsages()
		{
			const string statementText = "SELECT \"SELECTION\".RESPONDENTBUCKET_ID, PROJECT_ID FROM SELECTION WHERE SELECTION.NAME = NAME GROUP BY SELECTION.RESPONDENTBUCKET_ID, PROJECT_ID HAVING COUNT(SELECTION.SELECTION_ID) = COUNT(SELECTION_ID)";

			var foundSegments = FindUsagesOrdered(statementText, 8);
			foundSegments.Count.ShouldBe(5);
			foundSegments[0].Length.ShouldBe("\"SELECTION\"".Length);
			foundSegments[1].Length.ShouldBe("SELECTION".Length);
		}

		[Test(Description = @""), STAThread]
		public void TestFindObjectWithAliasUsages()
		{
			const string statementText = "SELECT S.RESPONDENTBUCKET_ID, PROJECT_ID FROM SELECTION \"S\" WHERE S.NAME = NAME GROUP BY S.RESPONDENTBUCKET_ID, PROJECT_ID HAVING COUNT(S.SELECTION_ID) = COUNT(SELECTION_ID)";

			var foundSegments = FindUsagesOrdered(statementText, 56);
			foundSegments.Count.ShouldBe(6);
			foundSegments[0].Length.ShouldBe("S".Length);
			foundSegments[1].Length.ShouldBe("SELECTION".Length);
			foundSegments[2].Length.ShouldBe("\"S\"".Length);
			foundSegments[3].Length.ShouldBe("S".Length);
		}

		[Test(Description = @""), STAThread]
		public void TestFindSchemaUsages()
		{
			const string statementText = "SELECT HUSQVIK.SELECTION.PROJECT_ID FROM (SELECT HUSQVIK.SELECTION.NAME FROM HUSQVIK.SELECTION), HUSQVIK.SELECTION";

			var foundSegments = FindUsagesOrdered(statementText, 9);
			foundSegments.Count.ShouldBe(4);
			foundSegments.ForEach(s => s.Length.ShouldBe("HUSQVIK".Length));
		}

		[Test(Description = @""), STAThread]
		public void TestBasicFindColumnUsages()
		{
			var foundSegments = FindUsagesOrdered(FindUsagesStatementText, 11);
			foundSegments.Count.ShouldBe(4);
			foundSegments[0].IndextStart.ShouldBe(9);
			foundSegments[1].IndextStart.ShouldBe(106);
			foundSegments[2].IndextStart.ShouldBe(262);
			foundSegments[3].IndextStart.ShouldBe(709);
			foundSegments.ForEach(s => s.Length.ShouldBe(4));
		}

		[Test(Description = @""), STAThread]
		public void TestFindColumnUsagesOfColumnAliases()
		{
			var foundSegments = FindUsagesOrdered(FindUsagesStatementText, 40);
			foundSegments.Count.ShouldBe(6);
			foundSegments[0].IndextStart.ShouldBe(37);
			foundSegments[0].Length.ShouldBe(5);
			foundSegments[1].IndextStart.ShouldBe(173);
			foundSegments[1].Length.ShouldBe(12);
			foundSegments[2].IndextStart.ShouldBe(186);
			foundSegments[2].Length.ShouldBe(5);
			foundSegments[3].IndextStart.ShouldBe(304);
			foundSegments[3].Length.ShouldBe(4);
			foundSegments[4].IndextStart.ShouldBe(309);
			foundSegments[4].Length.ShouldBe(12);
			foundSegments[5].IndextStart.ShouldBe(731);
			foundSegments[5].Length.ShouldBe(4);
		}

		[Test(Description = @""), STAThread]
		public void TestFindColumnUsagesOfIndirectColumnReferenceAtAliasNode()
		{
			var foundSegments = FindUsagesOrdered(FindUsagesStatementText, 25);
			foundSegments.Count.ShouldBe(2);
			foundSegments[0].IndextStart.ShouldBe(17);
			foundSegments[0].Length.ShouldBe(16);
			foundSegments[1].IndextStart.ShouldBe(152);
			foundSegments[1].Length.ShouldBe(16);
		}

		[Test(Description = @""), STAThread]
		public void TestFindColumnUsagesOfIndirectColumnReferenceAtColumnNode()
		{
			var foundSegments = FindUsagesOrdered(FindUsagesStatementText, 121);
			foundSegments.Count.ShouldBe(6);
			foundSegments[0].IndextStart.ShouldBe(115);
			foundSegments[0].Length.ShouldBe(16);
			foundSegments[1].IndextStart.ShouldBe(135);
			foundSegments[1].Length.ShouldBe(16);
			foundSegments[2].IndextStart.ShouldBe(275);
			foundSegments[2].Length.ShouldBe(4);
			foundSegments[3].IndextStart.ShouldBe(280);
			foundSegments[3].Length.ShouldBe(16);
			foundSegments[4].IndextStart.ShouldBe(612);
			foundSegments[4].Length.ShouldBe(4);
			foundSegments[5].IndextStart.ShouldBe(683);
			foundSegments[5].Length.ShouldBe(4);
		}

		[Test(Description = @""), STAThread]
		public void TestFindColumnUsagesOfComputedColumnAtUsage()
		{
			var foundSegments = FindUsagesOrdered(FindUsagesStatementText, 80);
			ValidateCommonResults3(foundSegments);
		}

		[Test(Description = @""), STAThread]
		public void TestFindColumnUsagesOfComputedColumnAtDefinition()
		{
			var foundSegments = FindUsagesOrdered(FindUsagesStatementText, 382);
			ValidateCommonResults3(foundSegments);
		}

		private void ValidateCommonResults3(List<TextSegment> foundSegments)
		{
			foundSegments.Count.ShouldBe(3);
			foundSegments[0].IndextStart.ShouldBe(71);
			foundSegments[1].IndextStart.ShouldBe(222);
			foundSegments[2].IndextStart.ShouldBe(375);
			foundSegments.ForEach(s => s.Length.ShouldBe(15));
		}

		[Test(Description = @""), STAThread]
		public void TestFindObjectUsagesAtCommonTableExpressionDefinition()
		{
			const string statement = "WITH CTE AS (SELECT SELECTION.NAME FROM SELECTION) SELECT CTE.NAME FROM CTE";

			var foundSegments = FindUsagesOrdered(statement, 6);
			ValidateCommonResults2(foundSegments);
		}

		[Test(Description = @""), STAThread]
		public void TestFindObjectUsagesAtCommonTableExpressionUsage()
		{
			const string statement = "WITH CTE AS (SELECT SELECTION.NAME FROM SELECTION) SELECT CTE.NAME FROM CTE";
			
			var foundSegments = FindUsagesOrdered(statement, 72);
			ValidateCommonResults2(foundSegments);
		}

		private void ValidateCommonResults2(List<TextSegment> foundSegments)
		{
			foundSegments.Count.ShouldBe(3);
			foundSegments[0].IndextStart.ShouldBe(5);
			foundSegments[1].IndextStart.ShouldBe(58);
			foundSegments[2].IndextStart.ShouldBe(72);
			foundSegments.ForEach(s => s.Length.ShouldBe(3));
		}

		[Test(Description = @""), STAThread]
		public void TestFindColumnUsagesAtJoinCondition()
		{
			var foundSegments = FindUsagesOrdered(FindUsagesStatementText, 602);
			ValidateCommonResults1(foundSegments);
		}

		[Test(Description = @""), STAThread]
		public void TestFindColumnUsagesInOrderByClause()
		{
			var foundSegments = FindUsagesOrdered(FindUsagesStatementText, 771);
			ValidateCommonResults1(foundSegments);
		}

		[Test(Description = @""), STAThread]
		public void TestFindColumnUsagesAtAliasDefinition()
		{
			var foundSegments = FindUsagesOrdered(FindUsagesStatementText, 350);
			ValidateCommonResults1(foundSegments);
		}

		[Test(Description = @""), STAThread]
		public void TestFindObjectUsageInOrderByClause()
		{
			var foundSegments = FindUsagesOrdered(FindUsagesStatementText, 767);
			foundSegments.Count.ShouldBe(2);
			foundSegments[0].IndextStart.ShouldBe(751);
			foundSegments[0].Length.ShouldBe(3);
			foundSegments[0].DisplayOptions.ShouldBe(DisplayOptions.Definition);
			foundSegments[1].IndextStart.ShouldBe(767);
			foundSegments[1].Length.ShouldBe(3);
			foundSegments[1].DisplayOptions.ShouldBe(DisplayOptions.Usage);
		}

		private void ValidateCommonResults1(IList<TextSegment> foundSegments)
		{
			foundSegments.Count.ShouldBe(6);
			foundSegments[0].IndextStart.ShouldBe(46);
			foundSegments[0].Length.ShouldBe(21);
			foundSegments[0].DisplayOptions.ShouldBe(DisplayOptions.Usage);
			foundSegments[1].IndextStart.ShouldBe(196);
			foundSegments[1].Length.ShouldBe(21);
			foundSegments[1].DisplayOptions.ShouldBe(DisplayOptions.Usage);
			foundSegments[2].IndextStart.ShouldBe(330);
			foundSegments[2].Length.ShouldBe(4);
			foundSegments[2].DisplayOptions.ShouldBe(DisplayOptions.Usage);
			foundSegments[3].IndextStart.ShouldBe(335);
			foundSegments[3].Length.ShouldBe(21);
			foundSegments[3].DisplayOptions.ShouldBe(DisplayOptions.Definition);
			foundSegments[4].IndextStart.ShouldBe(602);
			foundSegments[4].Length.ShouldBe(4);
			foundSegments[4].DisplayOptions.ShouldBe(DisplayOptions.Usage);
			foundSegments[5].IndextStart.ShouldBe(771);
			foundSegments[5].Length.ShouldBe(21);
			foundSegments[5].DisplayOptions.ShouldBe(DisplayOptions.Usage);
		}

		[Test(Description = @""), STAThread]
		public void TestAsteriskNotHighlightedWhenFindUsages()
		{
			const string statement = "SELECT NAME FROM (SELECT * FROM (SELECT NAME FROM SELECTION))";

			var foundSegments = FindUsagesOrdered(statement, 7);
			foundSegments.Count.ShouldBe(2);
			foundSegments[0].IndextStart.ShouldBe(7);
			foundSegments[1].IndextStart.ShouldBe(40);
		}

		[Test(Description = @""), STAThread]
		public void TestWrapCommonTableExpressionIntoAnotherCommonTableExpression()
		{
			_editor.Text = "WITH CTE1 AS (SELECT NAME FROM SELECTION) SELECT NAME FROM CTE1";
			_editor.CaretOffset = 15;

			ExecuteCommand(OracleCommands.WrapAsCommonTableExpression, new TestCommandSettings(new CommandSettingsModel { Value = "CTE2" } ));

			_editor.Text.ShouldBe(@"WITH CTE2 AS (SELECT NAME FROM SELECTION), CTE1 AS (SELECT CTE2.NAME FROM CTE2) SELECT NAME FROM CTE1");
		}

		[Test(Description = @""), STAThread]
		public void TestExpandAsteriskCommandWithObjectReference()
		{
			_editor.Text = "SELECT SELECTION.*, PROJECT.* FROM SELECTION, PROJECT";
			_editor.CaretOffset = 28;

			ExecuteCommand(OracleCommands.ExpandAsterisk, new TestCommandSettings(new CommandSettingsModel()));

			_editor.Text.ShouldBe("SELECT SELECTION.*, PROJECT.NAME, PROJECT.PROJECT_ID FROM SELECTION, PROJECT");
		}

		[Test(Description = @""), STAThread]
		public void TestExpandAsteriskCommandWithObjectReferenceOverDatabaseLink()
		{
			_editor.Text = "SELECT SELECTION.*, PROJECT.* FROM SELECTION, PROJECT@HQ_PDB_LOOPBACK";
			_editor.CaretOffset = 28;

			ExecuteCommand(OracleCommands.ExpandAsterisk, new TestCommandSettings(new CommandSettingsModel()));

			_editor.Text.ShouldBe("SELECT SELECTION.*, PROJECT.REMOTE_COLUMN1, PROJECT.\"RemoteColumn2\" FROM SELECTION, PROJECT@HQ_PDB_LOOPBACK");
		}

		[Test(Description = @""), STAThread]
		public void TestExpandAsteriskCommandWithAllColumns()
		{
			_editor.Text = "SELECT * FROM PROJECT, PROJECT P";
			_editor.CaretOffset = 7;

			ExecuteCommand(OracleCommands.ExpandAsterisk, new TestCommandSettings(new CommandSettingsModel()));

			_editor.Text.ShouldBe("SELECT PROJECT.NAME, PROJECT.PROJECT_ID, P.NAME, P.PROJECT_ID FROM PROJECT, PROJECT P");
		}

		[Test(Description = @""), STAThread]
		public void TestExpandAsteriskCommandWithAllColumnsOverDatabaseLink()
		{
			_editor.Text = "SELECT * FROM PROJECT@HQ_PDB_LOOPBACK, PROJECT@HQ_PDB_LOOPBACK P";
			_editor.CaretOffset = 7;

			ExecuteCommand(OracleCommands.ExpandAsterisk, new TestCommandSettings(new CommandSettingsModel()));

			_editor.Text.ShouldBe("SELECT PROJECT.REMOTE_COLUMN1, PROJECT.\"RemoteColumn2\", P.REMOTE_COLUMN1, P.\"RemoteColumn2\" FROM PROJECT@HQ_PDB_LOOPBACK, PROJECT@HQ_PDB_LOOPBACK P");
		}

		[Test(Description = @""), STAThread]
		public void TestFindGrammarSpecificFunctionUsages()
		{
			var foundSegments = FindUsagesOrdered(FindFunctionUsagesStatementText, 9);
			foundSegments.Count.ShouldBe(2);
			foundSegments[0].IndextStart.ShouldBe(9);
			foundSegments[1].IndextStart.ShouldBe(34);
			foundSegments.ForEach(s => s.Length.ShouldBe(5));
		}

		[Test(Description = @""), STAThread]
		public void TestFindGenericSqlFunctionUsages()
		{
			var foundSegments = FindUsagesOrdered(FindFunctionUsagesStatementText, 154);
			foundSegments.Count.ShouldBe(2);
			foundSegments[0].IndextStart.ShouldBe(154);
			foundSegments[1].IndextStart.ShouldBe(186);
			foundSegments.ForEach(s => s.Length.ShouldBe(8));
		}

		[Test(Description = @""), STAThread]
		public void TestFindLiteralUsages()
		{
			var foundSegments = FindUsagesOrdered(FindLiteralUsagesStatementText, 17);
			foundSegments.Count.ShouldBe(3);
			foundSegments[0].IndextStart.ShouldBe(16);
			foundSegments[1].IndextStart.ShouldBe(34);
			foundSegments[2].IndextStart.ShouldBe(120);
			foundSegments.ForEach(s => s.Length.ShouldBe(5));
		}

		[Test(Description = @""), STAThread]
		public void TestFindBindVariableUsages()
		{
			var foundSegments = FindUsagesOrdered(FindLiteralUsagesStatementText, 10);
			foundSegments.Count.ShouldBe(3);
			foundSegments[0].IndextStart.ShouldBe(10);
			foundSegments[1].IndextStart.ShouldBe(81);
			foundSegments[2].IndextStart.ShouldBe(137);
			foundSegments.ForEach(s => s.Length.ShouldBe(2));
		}

		[Test(Description = @""), STAThread]
		public void TestFindColumnUsagesWithAliasedColumnWithInvalidReference()
		{
			const string statement = "SELECT CPU_SECONDS X FROM (SELECT CPU_TIME / 1000000 CPU_SECONDS FROM V$SESSION)";

			var foundSegments = FindUsagesOrdered(statement, 10);
			foundSegments.Count.ShouldBe(3);
			foundSegments[0].IndextStart.ShouldBe(7);
			foundSegments[0].Length.ShouldBe(11);
			foundSegments[0].DisplayOptions.ShouldBe(DisplayOptions.Usage);
			foundSegments[1].IndextStart.ShouldBe(19);
			foundSegments[1].Length.ShouldBe(1);
			foundSegments[1].DisplayOptions.ShouldBe(DisplayOptions.Definition);
			foundSegments[2].IndextStart.ShouldBe(53);
			foundSegments[2].DisplayOptions.ShouldBe(DisplayOptions.Definition);
		}

		[Test(Description = @""), STAThread]
		public void TestUnnestCommand()
		{
			_editor.Text = @"SELECT IV.TEST_COLUMN || ' ADDED' FROM PROJECT, (SELECT SELECTION.NAME || ' FROM INLINE_VIEW ' TEST_COLUMN FROM SELECTION) IV, RESPONDENTBUCKET";
			_editor.CaretOffset = 50;

			CanExecuteCommand(OracleCommands.UnnestInlineView).ShouldBe(true);
			ExecuteCommand(OracleCommands.UnnestInlineView);

			_editor.Text.ShouldBe("SELECT SELECTION.NAME || ' FROM INLINE_VIEW ' || ' ADDED' FROM PROJECT, SELECTION, RESPONDENTBUCKET");
		}

		[Test(Description = @""), STAThread]
		public void TestUnnestCommandWithWhereClause()
		{
			_editor.Text = @"SELECT IV.TEST_COLUMN || ' ADDED' FROM PROJECT, (SELECT SELECTION.NAME || ' FROM INLINE_VIEW ' TEST_COLUMN FROM SELECTION WHERE SELECTION_ID = 123) IV, RESPONDENTBUCKET";
			_editor.CaretOffset = 50;

			CanExecuteCommand(OracleCommands.UnnestInlineView).ShouldBe(true);
			ExecuteCommand(OracleCommands.UnnestInlineView);

			_editor.Text.ShouldBe("SELECT SELECTION.NAME || ' FROM INLINE_VIEW ' || ' ADDED' FROM PROJECT, SELECTION, RESPONDENTBUCKET WHERE SELECTION_ID = 123");
		}

		[Test(Description = @""), STAThread]
		public void TestUnnestCommandWithCombinedWhereClause()
		{
			_editor.Text = @"SELECT * FROM (SELECT * FROM SELECTION WHERE SELECTION_ID = 123) IV, RESPONDENTBUCKET RB WHERE RB.RESPONDENTBUCKET_ID = 456";
			_editor.CaretOffset = 17;

			CanExecuteCommand(OracleCommands.UnnestInlineView).ShouldBe(true);
			ExecuteCommand(OracleCommands.UnnestInlineView);

			_editor.Text.ShouldBe("SELECT * FROM SELECTION, RESPONDENTBUCKET RB WHERE RB.RESPONDENTBUCKET_ID = 456 AND SELECTION_ID = 123");
		}

		[Test(Description = @""), STAThread]
		public void TestUnnestCommandWithAsterisk()
		{
			_editor.Text = @"SELECT IV.* FROM (SELECT * FROM SELECTION, RESPONDENTBUCKET) IV";
			_editor.CaretOffset = 18;

			CanExecuteCommand(OracleCommands.UnnestInlineView).ShouldBe(true);
			ExecuteCommand(OracleCommands.UnnestInlineView);

			_editor.Text.ShouldBe("SELECT * FROM SELECTION, RESPONDENTBUCKET");
		}

		[Test(Description = @""), STAThread]
		public void TestUnnestCommandWithObjectAsteriskCombinedWithOtherColumn()
		{
			_editor.Text = @"SELECT IV.*, TARGETGROUP_ID FROM (SELECT 1 C1, SELECTION.*, 3 C3 FROM SELECTION) IV, RESPONDENTBUCKET";
			_editor.CaretOffset = 40;

			CanExecuteCommand(OracleCommands.UnnestInlineView).ShouldBe(true);
			ExecuteCommand(OracleCommands.UnnestInlineView);

			_editor.Text.ShouldBe("SELECT 1 C1, SELECTION.*, 3 C3, TARGETGROUP_ID FROM SELECTION, RESPONDENTBUCKET");
		}

		[Test(Description = @""), STAThread]
		public void TestUnnestCommandWithWithInlineViewWithoutSpace()
		{
			_editor.Text = @"SELECT * FROM SELECTION JOIN(SELECT NAME FROM PROJECT) S ON SELECTION.NAME = S.NAME";
			_editor.CaretOffset = 30;

			CanExecuteCommand(OracleCommands.UnnestInlineView).ShouldBe(true);
			ExecuteCommand(OracleCommands.UnnestInlineView);

			_editor.Text.ShouldBe("SELECT * FROM SELECTION JOIN PROJECT ON SELECTION.NAME = PROJECT.NAME");
		}

		[Test(Description = @""), STAThread]
		public void TestUnnestCommandWithWithInlineViewWithObjectNamePrefix()
		{
			_editor.Text = @"SELECT * FROM SELECTION JOIN(SELECT PROJECT.NAME FROM PROJECT) S ON SELECTION.NAME = S.NAME";
			_editor.CaretOffset = 30;

			CanExecuteCommand(OracleCommands.UnnestInlineView).ShouldBe(true);
			ExecuteCommand(OracleCommands.UnnestInlineView);

			_editor.Text.ShouldBe("SELECT * FROM SELECTION JOIN PROJECT ON SELECTION.NAME = PROJECT.NAME");
		}

		[Test(Description = @""), STAThread]
		public void TestUnnestCommandWithColumnExpressions()
		{
			_editor.Text = @"SELECT 'OuterPrefix' || IV.VAL || 'OuterPostfix' FROM (SELECT 'InnerPrefix' || (DUMMY || 'InnerPostfix') VAL FROM DUAL) IV";
			_editor.CaretOffset = 60;

			CanExecuteCommand(OracleCommands.UnnestInlineView).ShouldBe(true);
			ExecuteCommand(OracleCommands.UnnestInlineView);

			_editor.Text.ShouldBe("SELECT 'OuterPrefix' || 'InnerPrefix' || (DUAL.DUMMY || 'InnerPostfix') || 'OuterPostfix' FROM DUAL");
		}

		[Test(Description = @""), STAThread]
		public void TestSafeDeleteCommandAtObjectAlias()
		{
			_editor.Text = @"SELECT S.RESPONDENTBUCKET_ID, S.SELECTION_ID, S.PROJECT_ID, S.NAME FROM SELECTION S";
			_editor.CaretOffset = 82;

			ExecuteCommand(SafeDeleteCommand.SafeDelete);

			_editor.Text.ShouldBe("SELECT SELECTION.RESPONDENTBUCKET_ID, SELECTION.SELECTION_ID, SELECTION.PROJECT_ID, SELECTION.NAME FROM SELECTION ");
		}

		[Test(Description = @""), STAThread]
		public void TestSafeDeleteCommandAtColumnAlias()
		{
			_editor.Text = @"SELECT XXX FROM (SELECT XXX FROM (SELECT XXX FROM (SELECT DUMMY XXX FROM DUAL) T1) T2) T3";
			_editor.CaretOffset = 64;

			ExecuteCommand(SafeDeleteCommand.SafeDelete);

			_editor.Text.ShouldBe("SELECT DUMMY FROM (SELECT DUMMY FROM (SELECT DUMMY FROM (SELECT DUMMY  FROM DUAL) T1) T2) T3");
		}

		[Test(Description = @""), STAThread]
		public void TestModifyCaseCommandWithMultipleTerminalsSelected()
		{
			_editor.Text = @"select null, 'null' from selection";
			_editor.CaretOffset = 3;
			_editor.SelectionLength = 28;

			ExecuteCommand(ModifyCaseCommand.MakeUpperCase);

			_editor.Text.ShouldBe("selECT NULL, 'null' FROM SELECTion");

			ExecuteCommand(ModifyCaseCommand.MakeLowerCase);

			_editor.Text.ShouldBe("select null, 'null' from selection");
		}

		[Test(Description = @""), STAThread]
		public void TestModifyCaseCommandWithUnrecognizedGrammar()
		{
			_editor.Text = @"lot of invalid tokens preceding; select 'null' as ""null"" from dual and lot of invalid tokens following";
			_editor.CaretOffset = 0;
			_editor.SelectionLength = _editor.Text.Length;

			ExecuteCommand(ModifyCaseCommand.MakeUpperCase);

			_editor.Text.ShouldBe("LOT OF INVALID TOKENS PRECEDING; SELECT 'null' AS \"null\" FROM DUAL AND LOT OF INVALID TOKENS FOLLOWING");
		}

		[Test(Description = @""), STAThread]
		public void TestModifyCaseCommandWithSingleCaseUnsafeToken()
		{
			_editor.Text = @"SELECT 'null' FROM DUAL";
			_editor.CaretOffset = 7;
			_editor.SelectionLength = 6;

			ExecuteCommand(ModifyCaseCommand.MakeUpperCase);

			_editor.Text.ShouldBe("SELECT 'NULL' FROM DUAL");
		}

		[Test(Description = @""), STAThread]
		public void TestModifyCaseCommandWithCaseUnsafeTokenAsLastToken()
		{
			_editor.Text = @"select * from ""Accounts""";
			_editor.SelectAll();

			ExecuteCommand(ModifyCaseCommand.MakeUpperCase);

			_editor.Text.ShouldBe("SELECT * FROM \"Accounts\"");
		}

		[Test(Description = @""), STAThread]
		public void TestMoveContentCommandUp()
		{
			_editor.Text = @"SELECT 'NamePrefix' || NAME || 'NamePostfix', 'IdPrefix' || PROJECT_ID || 'IdPostfix' FROM PROJECT";
			_editor.CaretOffset = 66;

			ExecuteCommand(MoveContentCommand.MoveContentUp);

			_editor.Text.ShouldBe("SELECT 'IdPrefix' || PROJECT_ID || 'IdPostfix', 'NamePrefix' || NAME || 'NamePostfix' FROM PROJECT");
			_editor.CaretOffset.ShouldBe(27);
		}

		[Test(Description = @""), STAThread]
		public void TestMoveOrderByExpressionCommandUp()
		{
			_editor.Text = @"SELECT * FROM SELECTION ORDER BY 'NamePrefix' || NAME || 'NamePostfix', 'IdPrefix' || SELECTION_ID || 'IdPostfix', 'IdPrefix' || PROJECT_ID || 'IdPostfix'";
			_editor.CaretOffset = 86;

			ExecuteCommand(MoveContentCommand.MoveContentUp);

			_editor.Text.ShouldBe("SELECT * FROM SELECTION ORDER BY 'IdPrefix' || SELECTION_ID || 'IdPostfix', 'NamePrefix' || NAME || 'NamePostfix', 'IdPrefix' || PROJECT_ID || 'IdPostfix'");
			_editor.CaretOffset.ShouldBe(47);
		}

		[Test(Description = @""), STAThread]
		public void TestMoveOrderByExpressionCommandDown()
		{
			_editor.Text = @"SELECT * FROM SELECTION ORDER BY 'NamePrefix' || NAME || 'NamePostfix', 'IdPrefix' || SELECTION_ID || 'IdPostfix', 'IdPrefix' || PROJECT_ID || 'IdPostfix'";
			_editor.CaretOffset = 33;

			ExecuteCommand(MoveContentCommand.MoveContentDown);

			_editor.Text.ShouldBe("SELECT * FROM SELECTION ORDER BY 'IdPrefix' || SELECTION_ID || 'IdPostfix', 'NamePrefix' || NAME || 'NamePostfix', 'IdPrefix' || PROJECT_ID || 'IdPostfix'");
			_editor.CaretOffset.ShouldBe(76);
		}

		[Test(Description = @""), STAThread]
		public void TestMoveContentCommandDown()
		{
			_editor.Text = @"SELECT 'NamePrefix' || NAME || 'NamePostfix', 'IdPrefix' || PROJECT_ID || 'IdPostfix' FROM PROJECT";
			_editor.CaretOffset = 24;

			ExecuteCommand(MoveContentCommand.MoveContentDown);

			_editor.Text.ShouldBe("SELECT 'IdPrefix' || PROJECT_ID || 'IdPostfix', 'NamePrefix' || NAME || 'NamePostfix' FROM PROJECT");
			_editor.CaretOffset.ShouldBe(65);
		}

		[Test(Description = @""), STAThread]
		public void TestMoveContentCommandDownAtLastColumn()
		{
			_editor.Text = @"SELECT 'NamePrefix' || NAME || 'NamePostfix', 'IdPrefix' || PROJECT_ID || 'IdPostfix' FROM PROJECT";
			_editor.CaretOffset = 66;

			ExecuteCommand(MoveContentCommand.MoveContentDown);

			_editor.Text.ShouldBe("SELECT 'NamePrefix' || NAME || 'NamePostfix', 'IdPrefix' || PROJECT_ID || 'IdPostfix' FROM PROJECT");
			_editor.CaretOffset.ShouldBe(66);
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandWithoutExistingGroupByClause()
		{
			_editor.Text = @"SELECT SELECTION.PROJECT_ID, COUNT(*) PROJECT_SELECTIONS FROM SELECTION";
			_editor.CaretOffset = 18;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddToGroupByClause);

			_editor.Text.ShouldBe("SELECT SELECTION.PROJECT_ID, COUNT(*) PROJECT_SELECTIONS FROM SELECTION GROUP BY SELECTION.PROJECT_ID");
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandWithMultipleSelectColumns()
		{
			_editor.Text = @"SELECT PROJECT_ID, RESPONDENTBUCKET_ID, COUNT(*) PROJECT_SELECTIONS FROM SELECTION";
			_editor.SelectionStart = 7;
			_editor.SelectionLength = 30;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddToGroupByClause);

			_editor.Text.ShouldBe("SELECT PROJECT_ID, RESPONDENTBUCKET_ID, COUNT(*) PROJECT_SELECTIONS FROM SELECTION GROUP BY PROJECT_ID, RESPONDENTBUCKET_ID");
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandWithSameExpressionWithinExistingGroupByClause()
		{
			_editor.Text = @"SELECT PROJECT_ID, COUNT(*) SELECTION_COUNT FROM SELECTION GROUP BY PROJECT_ID";
			_editor.CaretOffset = 7;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandNotAvailableAtAsteriskTerminal()
		{
			_editor.Text = @"SELECT * FROM SELECTION";
			_editor.CaretOffset = 7;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandWithWhereClause()
		{
			_editor.Text = @"SELECT PROJECT_ID, COUNT(*) PROJECT_SELECTIONS FROM SELECTION WHERE NAME LIKE '%1%'";
			_editor.SelectionStart = 7;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddToGroupByClause);

			_editor.Text.ShouldBe("SELECT PROJECT_ID, COUNT(*) PROJECT_SELECTIONS FROM SELECTION WHERE NAME LIKE '%1%' GROUP BY PROJECT_ID");
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandAtColumnTableQualifier()
		{
			_editor.Text = @"SELECT SELECTION.NAME, COUNT(*) FROM SELECTION JOIN RESPONDENTBUCKET ON SELECTION.RESPONDENTBUCKET_ID = RESPONDENTBUCKET.RESPONDENTBUCKET_ID";
			_editor.SelectionStart = 7;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandWithOrderByClause()
		{
			_editor.Text = @"SELECT PROJECT_ID, RESPONDENTBUCKET_ID, COUNT(*) PROJECT_SELECTIONS FROM SELECTION GROUP BY PROJECT_ID ORDER BY PROJECT_SELECTIONS";
			_editor.SelectionStart = 25;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddToGroupByClause);

			_editor.Text.ShouldBe("SELECT PROJECT_ID, RESPONDENTBUCKET_ID, COUNT(*) PROJECT_SELECTIONS FROM SELECTION GROUP BY PROJECT_ID, RESPONDENTBUCKET_ID ORDER BY PROJECT_SELECTIONS");
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandWithExpressionFollowedByOtherColumn()
		{
			_editor.Text = @"SELECT SELECTIONNAME || 'X', PROJECT_ID + 3 FROM SELECTION";
			_editor.SelectionStart = 7;
			_editor.SelectionLength = 20;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddToGroupByClause);

			_editor.Text.ShouldBe("SELECT SELECTIONNAME || 'X', PROJECT_ID + 3 FROM SELECTION GROUP BY SELECTIONNAME || 'X'");
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandWithExistingGroupByClause()
		{
			_editor.Text = @"SELECT SELECTION.PROJECT_ID, SELECTION.RESPONDENTBUCKET_ID, COUNT(*) SELECTION_COUNT FROM SELECTION GROUP BY SELECTION.PROJECT_ID";
			_editor.CaretOffset = 40;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddToGroupByClause);

			_editor.Text.ShouldBe("SELECT SELECTION.PROJECT_ID, SELECTION.RESPONDENTBUCKET_ID, COUNT(*) SELECTION_COUNT FROM SELECTION GROUP BY SELECTION.PROJECT_ID, SELECTION.RESPONDENTBUCKET_ID");
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandNotAvailableAtInvalidExpression()
		{
			_editor.Text = @"SELECT 1 + SELECTION_ID FROM SELECTION";
			_editor.SelectionStart = 7;
			_editor.SelectionLength = 3;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandNotAvailableAtBindVariable()
		{
			_editor.Text = @"SELECT :X FROM SELECTION";
			_editor.CaretOffset = 8;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandNotAvailableWhenSequencePseudoColumnWithinSelection()
		{
			_editor.Text = @"SELECT TEST_SEQ.NEXTVAL FROM SELECTION";
			_editor.CaretOffset = 18;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestAddToGroupByCommandNotAvailableWhenSequenceWithinSelection()
		{
			_editor.Text = @"SELECT TEST_SEQ.NEXTVAL FROM SELECTION";
			_editor.CaretOffset = 10;

			CanExecuteCommand(OracleCommands.AddToGroupByClause).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestToggleFullyQualifiedReferences()
		{
			_editor.Text = @"SELECT SQLPAD_FUNCTION, RESPONDENTBUCKET_ID, SELECTION_ID, PROJECT_ID, NAME, SQLPAD.SQLPAD_FUNCTION(0), TO_CHAR('') FROM SELECTION";

			ExecuteCommand(OracleCommands.ToggleFullyQualifiedReferences);

			_editor.Text.ShouldBe("SELECT HUSQVIK.SQLPAD_FUNCTION, HUSQVIK.SELECTION.RESPONDENTBUCKET_ID, HUSQVIK.SELECTION.SELECTION_ID, HUSQVIK.SELECTION.PROJECT_ID, HUSQVIK.SELECTION.NAME, HUSQVIK.SQLPAD.SQLPAD_FUNCTION(0), TO_CHAR('') FROM HUSQVIK.SELECTION");
		}

		[Test(Description = @""), STAThread]
		public void TestToggleFullyQualifiedReferencesWithAsteriskClause()
		{
			_editor.Text = @"SELECT * FROM SELECTION";

			ExecuteCommand(OracleCommands.ToggleFullyQualifiedReferences);

			_editor.Text.ShouldBe("SELECT * FROM HUSQVIK.SELECTION");
		}

		[Test(Description = @""), STAThread]
		public void TestToggleFullyQualifiedReferencesWithPartiallyQualifiedAsteriskClause()
		{
			_editor.Text = @"SELECT SELECTION.*, PROJECT.* FROM SELECTION, PROJECT";

			ExecuteCommand(OracleCommands.ToggleFullyQualifiedReferences);

			_editor.Text.ShouldBe("SELECT HUSQVIK.SELECTION.*, HUSQVIK.PROJECT.* FROM HUSQVIK.SELECTION, HUSQVIK.PROJECT");
		}

		[Test(Description = @""), STAThread]
		public void TestToggleFullyQualifiedReferencesWithNotExistingTable()
		{
			_editor.Text = @"SELECT NOT_EXISTING_TABLE.* FROM NOT_EXISTING_TABLE";

			CanExecuteCommand(OracleCommands.ToggleFullyQualifiedReferences).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestToggleFullyQualifiedReferencesWithAliasedTable()
		{
			_editor.Text = @"SELECT DUMMY, NAME FROM DUAL D, SELECTION S";
			_editor.SelectionLength = 0;

			ExecuteCommand(OracleCommands.ToggleFullyQualifiedReferences);

			_editor.Text.ShouldBe("SELECT D.DUMMY, S.NAME FROM DUAL D, HUSQVIK.SELECTION S");
		}

		[Test(Description = @""), STAThread]
		public void TestToggleFullyQualifiedReferencesWithRowIdPseudoColumn()
		{
			_editor.Text = @"SELECT ROWID FROM DUAL";
			_editor.SelectionLength = 0;

			ExecuteCommand(OracleCommands.ToggleFullyQualifiedReferences);

			_editor.Text.ShouldBe("SELECT DUAL.ROWID FROM DUAL");
		}

		[Test(Description = @""), STAThread]
		public void TestToggleFullyQualifiedReferencesOnFullyQualifiedSchemaFunction()
		{
			_editor.Text = @"SELECT HUSQVIK.SQLPAD_FUNCTION FROM SYS.DUAL";
			_editor.SelectionLength = 0;

			// TODO: Update when toogle off is implemented
			CanExecuteCommand(OracleCommands.ToggleFullyQualifiedReferences).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestToggleFullyQualifiedReferencesOnNonAliasedTableReference()
		{
			_editor.Text = @"SELECT DUMMY FROM (SELECT DUMMY FROM DUAL)";
			_editor.SelectionLength = 0;

			// TODO: Update when toogle off is implemented
			CanExecuteCommand(OracleCommands.ToggleFullyQualifiedReferences).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestResolveAmbiguousColumnCommand()
		{
			_editor.Text = @"SELECT DUAL.DUMMY FROM SYS.DUAL, ""PUBLIC"".DUAL";
			_editor.CaretOffset = 12;

			var actions = new OracleContextActionProvider().GetContextActions(TestFixture.DatabaseModel, _editor.Text, _editor.CaretOffset).Where(a => a.Name.StartsWith("Resolve as")).ToArray();

			actions.Length.ShouldBe(2);
			CanExecuteCommand(actions[0].ExecutionHandler).ShouldBe(true);
			ExecuteCommand(actions[0].ExecutionHandler);
			CanExecuteCommand(actions[1].ExecutionHandler).ShouldBe(true);

			_editor.Text.ShouldBe(@"SELECT SYS.DUAL.DUMMY FROM SYS.DUAL, ""PUBLIC"".DUAL");
		}

		[Test(Description = @""), STAThread]
		public void TestGenerateMissingColumnsCommand()
		{
			_editor.Text = @"SELECT NOT_EXISTING_COLUMN FROM SELECTION";
			_editor.CaretOffset = 7;

			CanExecuteCommand(OracleCommands.GenerateMissingColumns).ShouldBe(true);
			ExecuteCommand(OracleCommands.GenerateMissingColumns);

			_editor.Text.ShouldBe("SELECT NOT_EXISTING_COLUMN FROM SELECTION;\r\n\r\nALTER TABLE HUSQVIK.SELECTION ADD\r\n(\r\n\tNOT_EXISTING_COLUMN VARCHAR2(100) NULL\r\n);\r\n");
			_editor.CaretOffset.ShouldBe(105);
			_editor.SelectionLength.ShouldBe(18);
		}

		[Test(Description = @""), STAThread]
		public void TestCreateScriptCommand()
		{
			const string statementText = @"SELECT * FROM SELECTION";
			_editor.Text = statementText;
			_editor.CaretOffset = 17;

			CanExecuteCommand(OracleCommands.CreateScript).ShouldBe(true);
			ExecuteCommand(OracleCommands.CreateScript);

			var expectedResult = statementText + ";" + Environment.NewLine + Environment.NewLine + OracleTestDatabaseModel.SelectionTableCreateScript + ";";
			_editor.Text.ShouldBe(expectedResult);
			_editor.CaretOffset.ShouldBe(17);
		}

		[Test(Description = @""), STAThread]
		public void AddInsertIntoColumnListCommand()
		{
			const string statementText = @"INSERT INTO SELECTION SELECT * FROM SELECTION";
			_editor.Text = statementText;
			_editor.CaretOffset = 8;

			CanExecuteCommand(OracleCommands.AddInsertIntoColumnList).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddInsertIntoColumnList, new TestCommandSettings(new CommandSettingsModel { UseDefaultSettings = () => true } ));

			const string expectedResult = "INSERT INTO SELECTION (RESPONDENTBUCKET_ID, SELECTION_ID, PROJECT_ID, NAME) SELECT * FROM SELECTION";
			_editor.Text.ShouldBe(expectedResult);
			_editor.CaretOffset.ShouldBe(8);
		}

		[Test(Description = @""), STAThread]
		public void ReconfigureInsertIntoColumnList()
		{
			const string statementText = @"INSERT INTO SELECTION (RESPONDENTBUCKET_ID) SELECT * FROM SELECTION";
			_editor.Text = statementText;
			_editor.CaretOffset = 8;

			CanExecuteCommand(OracleCommands.AddInsertIntoColumnList).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddInsertIntoColumnList, new TestCommandSettings(new CommandSettingsModel { UseDefaultSettings = () => true } ));

			const string expectedResult = "INSERT INTO SELECTION (RESPONDENTBUCKET_ID, SELECTION_ID, PROJECT_ID, NAME) SELECT * FROM SELECTION";
			_editor.Text.ShouldBe(expectedResult);
			_editor.CaretOffset.ShouldBe(8);
		}

		[Test(Description = @""), STAThread]
		public void TestCleanRedundantSymbolCommand()
		{
			const string statementText = @"SELECT SELECTION.SELECTION_ID, HUSQVIK.RESPONDENTBUCKET.TARGETGROUP_ID, HUSQVIK.SELECTION.RESPONDENTBUCKET_ID FROM HUSQVIK.SELECTION, HUSQVIK.RESPONDENTBUCKET";
			_editor.Text = statementText;

			CanExecuteCommand(OracleCommands.CleanRedundantSymbol).ShouldBe(true);
			ExecuteCommand(OracleCommands.CleanRedundantSymbol);

			_editor.Text.ShouldBe("SELECT SELECTION_ID, TARGETGROUP_ID, SELECTION.RESPONDENTBUCKET_ID FROM SELECTION, RESPONDENTBUCKET");
			_editor.CaretOffset.ShouldBe(0);
		}

		[Test(Description = @""), STAThread]
		public void TestCleanRedundantProgramQualifier()
		{
			const string statementText = @"SELECT HUSQVIK.INVALID_OBJECT_TYPE(), SYS.XMLTYPE('<root/>'), HUSQVIK.SQLPAD.SQLPAD_FUNCTION(), SYS.DBMS_RANDOM.VALUE, HUSQVIK.TEST_SEQ.NEXTVAL FROM SYS.DUAL";
			_editor.Text = statementText;

			CanExecuteCommand(OracleCommands.CleanRedundantSymbol).ShouldBe(true);
			ExecuteCommand(OracleCommands.CleanRedundantSymbol);

			_editor.Text.ShouldBe("SELECT INVALID_OBJECT_TYPE(), XMLTYPE('<root/>'), SQLPAD.SQLPAD_FUNCTION(), DBMS_RANDOM.VALUE, TEST_SEQ.NEXTVAL FROM SYS.DUAL");
			_editor.CaretOffset.ShouldBe(0);
		}

		[Test(Description = @""), STAThread]
		public void TestCleanRedundantTerminalsAtQueryBlockLevelInOrderByClause()
		{
			const string statementText = @"SELECT * FROM SELECTION ORDER BY SELECTION.PROJECT_ID, SELECTION.NAME";
			_editor.Text = statementText;

			CanExecuteCommand(OracleCommands.CleanRedundantSymbol).ShouldBe(true);
			ExecuteCommand(OracleCommands.CleanRedundantSymbol);

			_editor.Text.ShouldBe("SELECT * FROM SELECTION ORDER BY PROJECT_ID, NAME");
			_editor.CaretOffset.ShouldBe(0);
		}

		[Test(Description = @""), STAThread]
		public void TestCleanSingleRedundantQualifier()
		{
			const string statementText = @"SELECT SYS.XMLTYPE('<root/>'), SYS.DBMS_RANDOM.VALUE FROM HUSQVIK.SELECTION";
			_editor.Text = statementText;
			_editor.CaretOffset = 8;

			CanExecuteCommand(OracleCommands.CleanRedundantSymbol).ShouldBe(true);
			ExecuteCommand(OracleCommands.CleanRedundantSymbol);

			_editor.Text.ShouldBe("SELECT XMLTYPE('<root/>'), SYS.DBMS_RANDOM.VALUE FROM HUSQVIK.SELECTION");
		}

		[Test(Description = @""), STAThread]
		public void TestGenerateCreateTableScriptFromQueryCommand()
		{
			const string statementText = @"SELECT * FROM DUAL";
			_editor.Text = statementText;

			CanExecuteCommand(OracleCommands.AddCreateTableAs).ShouldBe(true);
			var commandSettings = new TestCommandSettings(new CommandSettingsModel { Value = "NEW_TABLE" });
			commandSettings.GetSettingsCalled += (sender, args) => commandSettings.Settings.BooleanOptions[AddCreateTableAsCommand.CreateSeparateStatement].Value = true;

			ExecuteCommand(OracleCommands.AddCreateTableAs, commandSettings);

			const string expectedResult =
@"SELECT * FROM DUAL;

CREATE TABLE NEW_TABLE (
	DUMMY VARCHAR2(1 BYTE)
);
";
			_editor.Text.ShouldBe(expectedResult);
			_editor.CaretOffset.ShouldBe(0);
		}

		[Test(Description = @""), STAThread]
		public void TestGenerateCreateTableScriptFromInlineView()
		{
			const string statementText = @"SELECT * FROM (SELECT * FROM DUAL)";
			_editor.Text = statementText;
			_editor.CaretOffset = 18;

			CanExecuteCommand(OracleCommands.AddCreateTableAs).ShouldBe(true);
			var commandSettings = new TestCommandSettings(new CommandSettingsModel { Value = "NEW_TABLE" });
			commandSettings.GetSettingsCalled += (sender, args) => commandSettings.Settings.BooleanOptions[AddCreateTableAsCommand.CreateSeparateStatement].IsEnabled.ShouldBe(false);

			ExecuteCommand(OracleCommands.AddCreateTableAs, commandSettings);
		}

		[Test(Description = @""), STAThread]
		public void TestGenerateCreateTableAsSelectFromQueryCommand()
		{
			const string statementText = @"SELECT * FROM DUAL";
			_editor.Text = statementText;

			CanExecuteCommand(OracleCommands.AddCreateTableAs).ShouldBe(true);
			ExecuteCommand(OracleCommands.AddCreateTableAs, new TestCommandSettings(new CommandSettingsModel { Value = "NEW_TABLE" }));

			const string expectedResult =
@"CREATE TABLE NEW_TABLE (
	DUMMY
)
AS
SELECT * FROM DUAL";
			_editor.Text.ShouldBe(expectedResult);
			_editor.CaretOffset.ShouldBe(41);
		}

		[Test(Description = @""), STAThread]
		public void TestUnquoteCommand()
		{
			const string statementText = @"SELECT ""CaseSensitiveColumn"", ""CaseSensitiveColumn"" FROM INVOICELINES";
			_editor.Text = statementText;

			CanExecuteCommand(OracleCommands.Unquote).ShouldBe(true);
			ExecuteCommand(OracleCommands.Unquote);

			const string expectedResult = @"SELECT ""CaseSensitiveColumn"" CaseSensitiveColumn, ""CaseSensitiveColumn"" CaseSensitiveColumn FROM INVOICELINES";

			_editor.Text.ShouldBe(expectedResult);
			_editor.CaretOffset.ShouldBe(0);
		}

		[Test(Description = @""), STAThread]
		public void TestUnquoteCommandWithFullyQualifiedColumn()
		{
			const string statementText = @"SELECT ""CaseSensitiveTable"".""CaseSensitiveColumn"" FROM ""CaseSensitiveTable""";
			_editor.Text = statementText;

			CanExecuteCommand(OracleCommands.Unquote).ShouldBe(true);
			ExecuteCommand(OracleCommands.Unquote);

			const string expectedResult = @"SELECT ""CaseSensitiveTable"".""CaseSensitiveColumn"" CaseSensitiveColumn FROM ""CaseSensitiveTable""";

			_editor.Text.ShouldBe(expectedResult);
			_editor.CaretOffset.ShouldBe(0);
		}

		[Test(Description = @""), STAThread]
		public void TestUnquoteCommandWithAliasedExpression()
		{
			const string statementText = @"SELECT 1 + 1 ""CaseSensitiveColumn"" FROM DUAL";
			_editor.Text = statementText;

			CanExecuteCommand(OracleCommands.Unquote).ShouldBe(true);
			ExecuteCommand(OracleCommands.Unquote);

			const string expectedResult = @"SELECT 1 + 1 CaseSensitiveColumn FROM DUAL";

			_editor.Text.ShouldBe(expectedResult);
			_editor.CaretOffset.ShouldBe(0);
		}

		[Test(Description = @""), STAThread]
		public void TestUnquoteCommandWithExistingQuotedAlias()
		{
			const string statementText = @"SELECT ""CaseSensitiveColumn"" ""Alias"", ""CaseSensitiveColumn"" FROM INVOICELINES";
			_editor.Text = statementText;

			CanExecuteCommand(OracleCommands.Unquote).ShouldBe(true);
			ExecuteCommand(OracleCommands.Unquote);

			const string expectedResult = @"SELECT ""CaseSensitiveColumn"" Alias, ""CaseSensitiveColumn"" CaseSensitiveColumn FROM INVOICELINES";

			_editor.Text.ShouldBe(expectedResult);
		}

		[Test(Description = @""), STAThread]
		public void TestConvertSingleBindVariableToLiteralCommand()
		{
			const string statementText = @"SELECT :1, :1 FROM DUAL";
			_editor.Text = statementText;
			_editor.CaretOffset = 12;

			SetBindVariableAndExecute(0, "VALUE");

			const string expectedResult = @"SELECT :1, 'VALUE' FROM DUAL";

			_editor.Text.ShouldBe(expectedResult);
		}

		[Test(Description = @""), STAThread]
		public void TestConvertAllBindVariableOccurencesToLiteralCommand()
		{
			const string statementText = @"SELECT :1, :1 FROM DUAL";
			_editor.Text = statementText;
			_editor.CaretOffset = 12;

			SetBindVariableAndExecute(1, "2014-10-04", OracleBindVariable.DataTypeDate);

			const string expectedResult = @"SELECT DATE'2014-10-04', DATE'2014-10-04' FROM DUAL";

			_editor.Text.ShouldBe(expectedResult);
		}

		private void SetBindVariableAndExecute(int actionIndex, string value, string dataType = OracleBindVariable.DataTypeVarchar2)
		{
			var actions = new OracleContextActionProvider()
					.GetContextActions(TestFixture.DatabaseModel, _editor.Text, _editor.CaretOffset)
					.Where(a => a.Name.StartsWith("Convert"))
					.ToArray();

			actions.Length.ShouldBe(2);

			var action = actions[actionIndex];
			action.ExecutionContext.DocumentRepository.Statements.Count.ShouldBe(1);
			var bindVariable = action.ExecutionContext.DocumentRepository.Statements.Single().BindVariables.Single();
			bindVariable.DataType = dataType;
			bindVariable.Value = value;
			action.ExecutionHandler.CanExecuteHandler(action.ExecutionContext).CanExecute.ShouldBe(true);
			ExecuteCommand(action.ExecutionHandler, action.ExecutionContext);
		}

		[Test(Description = @""), STAThread]
		public void TestConvertSingleLiteralToBindVariableCommand()
		{
			const string statementText = @"SELECT 'VALUE', 'VALUE' FROM DUAL";
			_editor.Text = statementText;
			_editor.CaretOffset = 20;

			ExecuteConvertLiteralToBindVariableCommmand(0);

			const string expectedResult = @"SELECT 'VALUE', :BIND_VARIABLE FROM DUAL";

			_editor.Text.ShouldBe(expectedResult);
		}

		[Test(Description = @""), STAThread]
		public void TestConvertAllLiteralOccurencesToBindVariableCommand()
		{
			const string statementText = @"SELECT DATE'2014-10-04', DATE'2014-10-04', TIMESTAMP'2014-10-04', '2014-10-04' FROM DUAL";
			_editor.Text = statementText;
			_editor.CaretOffset = 15;

			ExecuteConvertLiteralToBindVariableCommmand(1);

			const string expectedResult = @"SELECT :BIND_VARIABLE, :BIND_VARIABLE, TIMESTAMP'2014-10-04', '2014-10-04' FROM DUAL";

			_editor.Text.ShouldBe(expectedResult);
		}

		private void ExecuteConvertLiteralToBindVariableCommmand(int actionIndex)
		{
			var actions = new OracleContextActionProvider()
				.GetContextActions(TestFixture.DatabaseModel, _editor.Text, _editor.CaretOffset)
				.Where(a => a.Name.StartsWith("Convert"))
				.ToArray();

			actions.Length.ShouldBe(2);
			var action = actions[actionIndex];
			action.ExecutionContext.SettingsProvider = new TestCommandSettings(new CommandSettingsModel { Value = "BIND_VARIABLE" });
			action.ExecutionHandler.CanExecuteHandler(action.ExecutionContext).CanExecute.ShouldBe(true);
			ExecuteCommand(action.ExecutionHandler, action.ExecutionContext);
		}

		[Test(Description = @""), STAThread]
		public void TestPropagateCommand()
		{
			const string statementText = @"SELECT 1 C1 FROM (SELECT 2 C2 FROM DUAL)";
			_editor.Text = statementText;
			_editor.CaretOffset = 28;

			CanExecuteCommand(OracleCommands.PropagateColumn).ShouldBe(true);
			ExecuteCommand(OracleCommands.PropagateColumn);

			const string expectedResult = @"SELECT 1 C1, C2 FROM (SELECT 2 C2 FROM DUAL)";

			_editor.Text.ShouldBe(expectedResult);
		}

		[Test(Description = @""), STAThread]
		public void TestPropagateCommandWithGrandParentWithAsterisk()
		{
			const string statementText = @"SELECT * FROM (SELECT 1 FROM (SELECT 1 C FROM DUAL))";
			_editor.Text = statementText;
			_editor.CaretOffset = 37;

			CanExecuteCommand(OracleCommands.PropagateColumn).ShouldBe(true);
			ExecuteCommand(OracleCommands.PropagateColumn);

			const string expectedResult = @"SELECT * FROM (SELECT 1, C FROM (SELECT 1 C FROM DUAL))";

			_editor.Text.ShouldBe(expectedResult);
		}

		[Test(Description = @""), STAThread]
		public void TestPropagateCommandNotAvailable()
		{
			const string statementText = @"SELECT SELECTION_ID FROM SELECTION";
			_editor.Text = statementText;
			_editor.CaretOffset = 8;

			CanExecuteCommand(OracleCommands.PropagateColumn).ShouldBe(false);
		}

		[Test(Description = @""), STAThread]
		public void TestPropagateCommandWithWithoutAlias()
		{
			const string statementText = @"SELECT 1 FROM (SELECT 1 FROM DUAL)";
			_editor.Text = statementText;
			_editor.CaretOffset = 22;

			CanExecuteCommand(OracleCommands.PropagateColumn).ShouldBe(true);
			ExecuteCommand(OracleCommands.PropagateColumn, new TestCommandSettings(new CommandSettingsModel { Value = "COLUMN1" }));

			const string expectedResult = @"SELECT 1, COLUMN1 FROM (SELECT 1 COLUMN1 FROM DUAL)";

			_editor.Text.ShouldBe(expectedResult);
		}
	}
}
