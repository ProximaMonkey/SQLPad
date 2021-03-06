﻿using System.Linq;
using NUnit.Framework;
using Shouldly;

using NonTerminals = SqlPad.Oracle.OracleGrammarDescription.NonTerminals;

namespace SqlPad.Oracle.Test
{
	[TestFixture]
	public class OraclePlSqlStatementValidatorTest
	{
		private static readonly OracleSqlParser Parser = OracleSqlParser.Instance;

		[Test]
		public void TestProgramNodeValidityWithinNestedStatement()
		{
			const string plsqlText =
@"BEGIN
	FOR i IN 1..2 LOOP
		dbms_output.put_line(a => 'x');
	END LOOP;
END;";

			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);
			validationModel.ProgramNodeValidity.Values.Count(v => !v.IsRecognized).ShouldBe(0);
		}

		[Test]
		public void TestExceptionIdentifierValidities()
		{
			const string plsqlText =
@"DECLARE
    test_exception EXCEPTION;
BEGIN
    RAISE test_exception;
    RAISE undefined_exception;
    EXCEPTION
    	WHEN test_exception OR undefined_exception THEN NULL;
    	WHEN OTHERS THEN NULL;
END;";
			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);
			var nodeValidities = validationModel.IdentifierNodeValidity.Values.ToArray();
			nodeValidities.Length.ShouldBe(2);
			nodeValidities[0].IsRecognized.ShouldBe(false);
			nodeValidities[0].Node.Token.Value.ShouldBe("undefined_exception");
			nodeValidities[1].IsRecognized.ShouldBe(false);
			nodeValidities[1].Node.Token.Value.ShouldBe("undefined_exception");
		}

		[Test]
		public void TestOthersExceptionCombinedWithNamedException()
		{
			const string plsqlText =
@"DECLARE
    test_exception EXCEPTION;
BEGIN
    NULL;
    EXCEPTION
    	WHEN test_exception OR OTHERS THEN NULL;
END;";
			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);
			validationModel.IdentifierNodeValidity.Count.ShouldBe(0);
			var nodeValidities = validationModel.InvalidNonTerminals.Values.ToArray();
			nodeValidities.Length.ShouldBe(1);
			nodeValidities[0].Node.Token.Value.ShouldBe("OTHERS");
			nodeValidities[0].SemanticErrorType.ShouldBe(OracleSemanticErrorType.PlSql.NoChoicesMayAppearWithChoiceOthersInExceptionHandler);
		}

		[Test]
		public void TestPlSqlBuiltInDataTypes()
		{
			const string plsqlText =
@"DECLARE
	test_value1 BINARY_INTEGER;
	test_value2 PLS_INTEGER;
	test_value3 BOOLEAN := TRUE;
BEGIN
	NULL;
END;";
			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);
			validationModel.IdentifierNodeValidity.Count.ShouldBe(0);
		}

		[Test]
		public void TestUndefinedAndInvalidAssociativeArrayIndexTypes()
		{
			const string plsqlText =
@"DECLARE
	TYPE test_table_type1 IS TABLE OF NUMBER INDEX BY undefined_type;
	TYPE test_table_type2 IS TABLE OF NUMBER INDEX BY boolean;
	TYPE test_table_type3 IS TABLE OF NUMBER INDEX BY varchar2(30);
BEGIN
	NULL;
END;";
			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);
			validationModel.IdentifierNodeValidity.Count.ShouldBe(1);
			validationModel.InvalidNonTerminals.Count.ShouldBe(1);
			var validationData = validationModel.InvalidNonTerminals.Values.First();
			validationData.SemanticErrorType.ShouldBe(OracleSemanticErrorType.PlSql.UnsupportedTableIndexType);
		}

		[Test]
		public void TestParametrizedPackageProcedureInvokation()
		{
			const string plsqlText =
@"DECLARE
	PROCEDURE test_procedure2(p BOOLEAN) IS BEGIN NULL; END;
BEGIN
	test_procedure2(p => TRUE);
END;";
			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);
			validationModel.ProgramNodeValidity.Count.ShouldBe(1);
			var validationData = validationModel.ProgramNodeValidity.Values.First();
			validationData.IsRecognized.ShouldBe(true);
			validationData.SemanticErrorType.ShouldBe(null);
		}

		[Test]
		public void TestStatementValidationWithFunctionHavingSameParameterNameAtDifferentDataLevels()
		{
			const string plsqlText =
@"DECLARE
	l_http_request utl_http.req;
BEGIN
	l_http_request := utl_http.begin_request(url => 'https://github.com/Husqvik.atom');
END;";
			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			Should.NotThrow(() => OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement));
		}

		[Test]
		public void TestPlSqlVarcharMaximumPrecision()
		{
			const string plsqlText =
@"DECLARE
	value1 VARCHAR2(32767);
	value2 NVARCHAR2(16383);
	value3 RAW(32767);
BEGIN
	NULL;
END;";
			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);
			validationModel.InvalidNonTerminals.Count.ShouldBe(0);
		}

		[Test]
		public void TestForUpdateWithGroupByWithinPlSql()
		{
			const string plsqlText =
@"DECLARE
	dummy VARCHAR2(1);
BEGIN
	SELECT dummy INTO dummy FROM dual GROUP BY dummy FOR UPDATE;
END;";
			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(1);
			var validationData = validationModel.InvalidNonTerminals.Values.First();
			validationData.SemanticErrorType.ShouldBe(OracleSemanticErrorType.ForUpdateNotAllowed);
			validationData.Node.Id.ShouldBe(NonTerminals.ForUpdateClause);
		}

		[Test]
		public void TestMissingSelectIntoClause()
		{
			const string plsqlText = @"BEGIN SELECT dummy FROM dual; END;";
			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(1);
			var validationData = validationModel.InvalidNonTerminals.Values.First();
			validationData.SemanticErrorType.ShouldBe(OracleSemanticErrorType.PlSql.IntoClauseExpected);
			validationData.Node.Id.ShouldBe(NonTerminals.SelectList);
		}

		[Test]
		public void TestMissingSelectIntoClauseWithinInlineView()
		{
			const string plsqlText = @"BEGIN UPDATE (SELECT 1 c1, 2 c2 FROM dual) SET c2 = NULL; END;";
			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(0);
		}

		[Test]
		public void TestTooManyValuesToIntoClause()
		{
			const string plsqlText =
				@"DECLARE
  variable VARCHAR2(30);
BEGIN
  SELECT dummy, dummy INTO variable FROM dual;
END;";

			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(1);
			var validationData = validationModel.InvalidNonTerminals.Values.First();
			validationData.SemanticErrorType.ShouldBe(OracleSemanticErrorType.PlSql.TooManyValues);
			validationData.Node.Id.ShouldBe(NonTerminals.BindVariableExpressionOrPlSqlTargetList);
		}


		[Test]
		public void TestNotEnoughValuesToIntoClause()
		{
			const string plsqlText =
				@"DECLARE
  variable VARCHAR2(30);
BEGIN
  SELECT dummy INTO variable, variable FROM dual;
END;";

			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(1);
			var validationData = validationModel.InvalidNonTerminals.Values.First();
			validationData.SemanticErrorType.ShouldBe(OracleSemanticErrorType.PlSql.NotEnoughValues);
			validationData.Node.Id.ShouldBe(NonTerminals.BindVariableExpressionOrPlSqlTargetList);
		}

		[Test]
		public void TestReadOnlyAssignTarget()
		{
			const string plsqlText =
				@"CREATE PROCEDURE test_procedure (p IN VARCHAR2)
IS
BEGIN
	p := '';
END;";

			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(1);
			var validationData = validationModel.InvalidNonTerminals.Values.First();
			validationData.SemanticErrorType.ShouldBe(OracleSemanticErrorType.PlSql.ExpressionCannotBeUsedAsAssignmentTarget);
			validationData.Node.Id.ShouldBe(NonTerminals.AssignmentStatementTarget);
		}

		[Test]
		public void TestNotEnoughValuesUsingAsteriskClauseWithUnrecognizedRowSource()
		{
			const string plsqlText =
				@"DECLARE
  variable VARCHAR2(30);
BEGIN
  SELECT * INTO variable FROM non_existing_table;
  SELECT non_existing_table.* INTO variable FROM non_existing_table;
END;";

			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(0);
		}

		[Test]
		public void TestNotEnoughValuesUsingAsteriskClauseWithUnrecognizedNestedRowSource()
		{
			const string plsqlText =
				@"DECLARE
  variable VARCHAR2(30);
BEGIN
  SELECT * INTO variable FROM (SELECT * FROM non_existing_table);
  SELECT row_source.* INTO variable FROM (SELECT * FROM non_existing_table) row_source;
END;";

			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(0);
		}

		[Test]
		public void TestWrongNumberOfValuesInIntoListOfFetchStatement()
		{
			const string plsqlText =
				@"DECLARE
	test_variable VARCHAR2(1);
	CURSOR test_cursor is SELECT dummy, dummy FROM dual;
BEGIN
	OPEN test_cursor;
	FETCH test_cursor INTO test_variable;
END;";

			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(1);
			var validationData = validationModel.InvalidNonTerminals.Values.First();
			validationData.SemanticErrorType.ShouldBe(OracleSemanticErrorType.PlSql.WrongNumberOfValuesInIntoListOfFetchStatement);
			validationData.Node.Id.ShouldBe(NonTerminals.BindVariableExpressionOrPlSqlTargetList);
		}

		[Test]
		public void TestSelectIntoClauseWithOpenCursor()
		{
			const string plsqlText = @"BEGIN OPEN :c1 FOR SELECT * FROM dual; END;";

			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(0);
		}

		[Test]
		public void TestSysRefCursorDataType()
		{
			const string plsqlText =
				@"DECLARE
	c SYS_REFCURSOR;
BEGIN
	OPEN c FOR SELECT * FROM dual;
	dbms_sql.return_result(c);
END;";

			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.IdentifierNodeValidity.Count.ShouldBe(0);
		}

		[Test]
		public void TestSelectIntoClauseWithinImplicitCursorDefinition()
		{
			const string plsqlText =
				@"BEGIN
    FOR c IN (SELECT dummy FROM dual) LOOP
        NULL;
    END LOOP;
END;";

			var statement = Parser.Parse(plsqlText).Single();
			statement.ParseStatus.ShouldBe(ParseStatus.Success);

			var validationModel = OracleStatementValidatorTest.BuildValidationModel(plsqlText, statement);

			validationModel.InvalidNonTerminals.Count.ShouldBe(0);
		}
	}
}
