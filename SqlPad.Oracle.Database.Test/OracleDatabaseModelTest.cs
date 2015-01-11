﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;
using SqlPad.Oracle.ExecutionPlan;
using SqlPad.Oracle.ModelDataProviders;
using SqlPad.Oracle.ToolTips;
using SqlPad.Test;

namespace SqlPad.Oracle.Database.Test
{
	[TestFixture]
	public class OracleDatabaseModelTest : TemporaryDirectoryTestFixture
	{
		private const string LoopbackDatabaseLinkName = "HQ_PDB";
		private readonly ConnectionStringSettings _connectionString = new ConnectionStringSettings("TestConnection", "DATA SOURCE=HQ_PDB_TCP;PASSWORD=oracle;USER ID=HUSQVIK");

		private const string ExplainPlanTestQuery =
@"SELECT /*+ gather_plan_statistics */
    *
FROM
    (SELECT ROWNUM VAL FROM DUAL) T1,
    (SELECT ROWNUM VAL FROM DUAL) T2,
    (SELECT ROWNUM VAL FROM DUAL) T3
WHERE
    T1.VAL = T2.VAL AND
    T2.VAL = T3.VAL";

		static OracleDatabaseModelTest()
		{
			OracleConfiguration.Configuration.ExecutionPlan.TargetTable.Name = "TOAD_PLAN_TABLE";
		}

		[Test]
		public void TestModelInitialization()
		{
			using (var databaseModel = OracleDatabaseModel.GetDatabaseModel(_connectionString))
			{
				databaseModel.Schemas.Count.ShouldBe(0);
				databaseModel.AllObjects.Count.ShouldBe(0);
				databaseModel.DatabaseLinks.Count.ShouldBe(0);
				databaseModel.CharacterSets.Count.ShouldBe(0);
				databaseModel.StatisticsKeys.Count.ShouldBe(0);
				databaseModel.SystemParameters.Count.ShouldBe(0);

				databaseModel.IsInitialized.ShouldBe(false);
				var refreshStartedResetEvent = new ManualResetEvent(false);
				var refreshFinishedResetEvent = new ManualResetEvent(false);
				databaseModel.RefreshStarted += (sender, args) => refreshStartedResetEvent.Set();
				databaseModel.RefreshCompleted += (sender, args) => refreshFinishedResetEvent.Set();
				databaseModel.Initialize();
				refreshStartedResetEvent.WaitOne();

				using (var modelClone = OracleDatabaseModel.GetDatabaseModel(_connectionString))
				{
					var cloneRefreshTask = modelClone.Refresh();
					refreshFinishedResetEvent.WaitOne();
					cloneRefreshTask.Wait();

					databaseModel.IsInitialized.ShouldBe(true);
					databaseModel.Schemas.Count.ShouldBeGreaterThan(0);

					Trace.WriteLine("Assert original database model");
					AssertDatabaseModel(databaseModel);

					Trace.WriteLine("Assert cloned database model");
					AssertDatabaseModel(modelClone);
				}
			}
		}

		private void AssertDatabaseModel(OracleDatabaseModelBase databaseModel)
		{
			databaseModel.VersionString.ShouldNotBe(null);

			databaseModel.VersionMajor.ShouldBeGreaterThan(8);

			databaseModel.AllObjects.Count.ShouldBeGreaterThan(0);
			Trace.WriteLine(String.Format("All object dictionary has {0} members. ", databaseModel.AllObjects.Count));
			
			databaseModel.DatabaseLinks.Count.ShouldBeGreaterThan(0);
			Trace.WriteLine(String.Format("Database link dictionary has {0} members. ", databaseModel.DatabaseLinks.Count));

			databaseModel.CharacterSets.Count.ShouldBeGreaterThan(0);
			Trace.WriteLine(String.Format("Character set collection has {0} members. ", databaseModel.CharacterSets.Count));

			databaseModel.StatisticsKeys.Count.ShouldBeGreaterThan(0);
			Trace.WriteLine(String.Format("Statistics key dictionary has {0} members. ", databaseModel.StatisticsKeys.Count));

			databaseModel.SystemParameters.Count.ShouldBeGreaterThan(0);
			Trace.WriteLine(String.Format("System parameters dictionary has {0} members. ", databaseModel.SystemParameters.Count));

			var objectForScriptCreation = databaseModel.GetFirstSchemaObject<OracleSchemaObject>(databaseModel.GetPotentialSchemaObjectIdentifiers("SYS", "OBJ$"));
			objectForScriptCreation.ShouldNotBe(null);
			var scriptTask = databaseModel.GetObjectScriptAsync(objectForScriptCreation, CancellationToken.None);
			scriptTask.Wait();

			Trace.WriteLine("Object script output: " + Environment.NewLine + scriptTask.Result + Environment.NewLine);

			scriptTask.Result.ShouldNotBe(null);
			scriptTask.Result.Length.ShouldBeGreaterThan(100);

			var executionModel =
				new StatementExecutionModel
				{
					StatementText = "SELECT /*+ gather_plan_statistics */ * FROM DUAL WHERE DUMMY = :1",
					BindVariables = new[] { new BindVariableModel(new BindVariableConfiguration { Name = "1", Value = "X" }) },
					GatherExecutionStatistics = true
				};
			
			var result = databaseModel.ExecuteStatement(executionModel);
			result.ExecutedSuccessfully.ShouldBe(true);
			result.AffectedRowCount.ShouldBe(-1);
			
			databaseModel.CanFetch.ShouldBe(false);

			var columnHeaders = result.ColumnHeaders.ToArray();
			columnHeaders.Length.ShouldBe(1);
			columnHeaders[0].DataType.ShouldBe(typeof(string));
			columnHeaders[0].DatabaseDataType.ShouldBe("Varchar2");
			columnHeaders[0].Name.ShouldBe("DUMMY");
			columnHeaders[0].ValueConverter.ShouldNotBe(null);

			var rows = result.InitialResultSet.ToArray();
			rows.Length.ShouldBe(1);
			rows[0].Length.ShouldBe(1);
			rows[0][0].ToString().ShouldBe("X");

			databaseModel.FetchRecords(1).Any().ShouldBe(false);

			var displayCursorTask = databaseModel.GetCursorExecutionStatisticsAsync(CancellationToken.None);
			displayCursorTask.Wait();

			var planItemCollection = displayCursorTask.Result;
			planItemCollection.PlanText.ShouldNotBe(null);

			Trace.WriteLine("Display cursor output: " + Environment.NewLine + planItemCollection.PlanText + Environment.NewLine);

			planItemCollection.PlanText.Length.ShouldBeGreaterThan(100);

			var task = databaseModel.GetExecutionStatisticsAsync(CancellationToken.None);
			task.Wait();

			var statisticsRecords = task.Result.Where(r => r.Value != 0).ToArray();
			statisticsRecords.Length.ShouldBeGreaterThan(0);

			var statistics = String.Join(Environment.NewLine, statisticsRecords.Select(r => String.Format("{0}: {1}", r.Name.PadRight(40), r.Value)));
			Trace.WriteLine("Execution statistics output: " + Environment.NewLine + statistics + Environment.NewLine);
		}

		#if !ORACLE_MANAGED_DATA_ACCESS_CLIENT
		[Test]
		public void TestDataTypesFetch()
		{
			var clobParameter = String.Join(" ", Enumerable.Repeat("CLOB DATA", 200));
			var executionModel =
					new StatementExecutionModel
					{
						StatementText = "SELECT TO_BLOB(RAWTOHEX('BLOB')), TO_CLOB('" + clobParameter + "'), TO_NCLOB('NCLOB DATA'), DATA_DEFAULT, TIMESTAMP'2014-11-01 14:16:32.123456789 CET' AT TIME ZONE '02:00', TIMESTAMP'2014-11-01 14:16:32.123456789', 0.1234567890123456789012345678901234567891, XMLTYPE('<root/>'), 1.23456789012345678901234567890123456789E-125, DATE'-4712-01-01' FROM ALL_TAB_COLS WHERE OWNER = 'SYS' AND TABLE_NAME = 'DUAL'",
						BindVariables = new BindVariableModel[0],
						GatherExecutionStatistics = true
					};

			using (var databaseModel = OracleDatabaseModel.GetDatabaseModel(_connectionString))
			{
				var result = databaseModel.ExecuteStatement(executionModel);
				
				result.ExecutedSuccessfully.ShouldBe(true);

				result.ColumnHeaders.Count.ShouldBe(10);
				result.ColumnHeaders[0].DatabaseDataType.ShouldBe("Blob");
				result.ColumnHeaders[1].DatabaseDataType.ShouldBe("Clob");
				result.ColumnHeaders[2].DatabaseDataType.ShouldBe("NClob");
				result.ColumnHeaders[3].DatabaseDataType.ShouldBe("Long");
				result.ColumnHeaders[4].DatabaseDataType.ShouldBe("TimeStampTZ");
				result.ColumnHeaders[5].DatabaseDataType.ShouldBe("TimeStamp");
				result.ColumnHeaders[6].DatabaseDataType.ShouldBe("Decimal");
				result.ColumnHeaders[7].DatabaseDataType.ShouldBe("XmlType");
				result.ColumnHeaders[8].DatabaseDataType.ShouldBe("Decimal");
				result.ColumnHeaders[9].DatabaseDataType.ShouldBe("Date");

				result.InitialResultSet.Count.ShouldBe(1);
				var firstRow = result.InitialResultSet[0];
				firstRow[0].ShouldBeTypeOf<OracleBlobValue>();
				var blobValue = (OracleBlobValue)firstRow[0];
				blobValue.Length.ShouldBe(4);
				blobValue.GetChunk(2).ShouldBe(new byte[] { 66, 76 });
				blobValue.Value.Length.ShouldBe(4);
				blobValue.ToString().ShouldBe("(BLOB[4 B])");
				firstRow[1].ShouldBeTypeOf<OracleClobValue>();
				var expectedPreview = clobParameter.Substring(0, 1023) + OracleLargeTextValue.Ellipsis;
				firstRow[1].ToString().ShouldBe(expectedPreview);
				((OracleClobValue)firstRow[1]).DataTypeName.ShouldBe("CLOB");
				firstRow[2].ShouldBeTypeOf<OracleClobValue>();
				var clobValue = (OracleClobValue)firstRow[2];
				clobValue.DataTypeName.ShouldBe("NCLOB");
				clobValue.Length.ShouldBe(20);
				clobValue.Value.ShouldBe("NCLOB DATA");
				firstRow[3].ShouldBeTypeOf<string>();
				firstRow[4].ShouldBeTypeOf<OracleTimestampWithTimeZone>();
				firstRow[4].ToString().ShouldBe("11/1/2014 3:16:32 PM.123456789 +02:00");
				firstRow[5].ShouldBeTypeOf<OracleTimestamp>();
				firstRow[5].ToString().ShouldBe("11/1/2014 2:16:32 PM.123456789");
				firstRow[6].ShouldBeTypeOf<OracleNumber>();
				firstRow[6].ToString().ShouldBe("0.1234567890123456789012345678901234567891");
				firstRow[7].ShouldBeTypeOf<OracleXmlValue>();
				var xmlValue = (OracleXmlValue)firstRow[7];
				xmlValue.Length.ShouldBe(8);
				xmlValue.Preview.ShouldBe("<root/>\u2026");
				firstRow[8].ShouldBeTypeOf<OracleNumber>();
				firstRow[8].ToString().ShouldBe("1.23456789012345678901234567890123456789E-125");
				firstRow[9].ShouldBeTypeOf<OracleDateTime>();
				firstRow[9].ToString().ShouldBe("BC 1/1/4712 12:00:00 AM");
			}
		}

		[Test]
		public void TestNullDataTypesFetch()
		{
			var executionModel =
					new StatementExecutionModel
					{
						StatementText = "SELECT EMPTY_BLOB(), EMPTY_CLOB(), CAST(NULL AS TIMESTAMP WITH TIME ZONE), CAST(NULL AS TIMESTAMP), CAST(NULL AS DATE), CAST(NULL AS DECIMAL), NVL2(XMLTYPE('<root/>'), null, XMLTYPE('<root/>')) FROM DUAL",
						BindVariables = new BindVariableModel[0],
						GatherExecutionStatistics = true
					};

			using (var databaseModel = OracleDatabaseModel.GetDatabaseModel(_connectionString))
			{
				var result = databaseModel.ExecuteStatement(executionModel);

				result.ExecutedSuccessfully.ShouldBe(true);

				result.ColumnHeaders.Count.ShouldBe(7);
				result.ColumnHeaders[0].DatabaseDataType.ShouldBe("Blob");
				result.ColumnHeaders[1].DatabaseDataType.ShouldBe("Clob");
				result.ColumnHeaders[2].DatabaseDataType.ShouldBe("TimeStampTZ");
				result.ColumnHeaders[3].DatabaseDataType.ShouldBe("TimeStamp");
				result.ColumnHeaders[4].DatabaseDataType.ShouldBe("Date");
				result.ColumnHeaders[5].DatabaseDataType.ShouldBe("Decimal");
				result.ColumnHeaders[6].DatabaseDataType.ShouldBe("XmlType");

				result.InitialResultSet.Count.ShouldBe(1);
				var firstRow = result.InitialResultSet[0];
				firstRow[0].ShouldBeTypeOf<OracleBlobValue>();
				var blobValue = (OracleBlobValue)firstRow[0];
				blobValue.Length.ShouldBe(0);
				blobValue.ToString().ShouldBe(String.Empty);
				firstRow[1].ShouldBeTypeOf<OracleClobValue>();
				firstRow[1].ToString().ShouldBe(String.Empty);
				((OracleClobValue)firstRow[1]).DataTypeName.ShouldBe("CLOB");
				firstRow[2].ShouldBeTypeOf<OracleTimestampWithTimeZone>();
				firstRow[2].ToString().ShouldBe(String.Empty);
				firstRow[3].ShouldBeTypeOf<OracleTimestamp>();
				firstRow[3].ToString().ShouldBe(String.Empty);
				firstRow[4].ShouldBeTypeOf<OracleDateTime>();
				firstRow[4].ToString().ShouldBe(String.Empty);
				firstRow[5].ShouldBeTypeOf<OracleNumber>();
				firstRow[5].ToString().ShouldBe(String.Empty);
				firstRow[6].ShouldBeTypeOf<OracleXmlValue>();
				firstRow[6].ToString().ShouldBe(String.Empty);
			}
		}
		#endif

		[Test]
		public void TestColumnDetailDataProvider()
		{
			var model = new ColumnDetailsModel();
			using (var databaseModel = OracleDatabaseModel.GetDatabaseModel(_connectionString))
			{
				databaseModel.Initialize().Wait();
				databaseModel.UpdateColumnDetailsAsync(new OracleObjectIdentifier(OracleDatabaseModelBase.SchemaSys, "\"DUAL\""), "\"DUMMY\"", model, CancellationToken.None).Wait();
			}

			model.AverageValueSize.ShouldBe(2);
			model.DistinctValueCount.ShouldBe(1);
			model.HistogramBucketCount.ShouldBe(1);
			model.HistogramType.ShouldBe("None");
			model.LastAnalyzed.ShouldBeGreaterThan(DateTime.MinValue);
			model.NullValueCount.ShouldBe(0);
			model.SampleSize.ShouldBe(1);
		}

		[Test]
		public void TestTableDetailDataProvider()
		{
			var model = new TableDetailsModel();

			using (var databaseModel = OracleDatabaseModel.GetDatabaseModel(_connectionString))
			{
				databaseModel.Initialize().Wait();
				databaseModel.UpdateTableDetailsAsync(new OracleObjectIdentifier(OracleDatabaseModelBase.SchemaSys, "\"DUAL\""), model, CancellationToken.None).Wait();
			}

			model.AverageRowSize.ShouldBe(2);
			model.BlockCount.ShouldBe(1);
			model.ClusterName.ShouldBe(String.Empty);
			model.Compression.ShouldBe("Disabled");
			model.IsPartitioned.ShouldBe(false);
			model.IsTemporary.ShouldBe(false);
			model.LastAnalyzed.ShouldBeGreaterThan(DateTime.MinValue);
			model.Organization.ShouldBe("Heap");
			model.ParallelDegree.ShouldBe("1");
			model.RowCount.ShouldBe(1);
		}

		[Test]
		public void TestRemoteTableColumnDataProvider()
		{
			IReadOnlyList<string> remoteTableColumns;

			using (var databaseModel = OracleDatabaseModel.GetDatabaseModel(_connectionString))
			{
				databaseModel.Initialize().Wait();
				var task = databaseModel.GetRemoteTableColumnsAsync(LoopbackDatabaseLinkName, new OracleObjectIdentifier(null, "\"USER_TABLES\""), CancellationToken.None);
				task.Wait();

				remoteTableColumns = task.Result;
			}

			remoteTableColumns.Count.ShouldBe(64);
			remoteTableColumns[0].ShouldBe("\"TABLE_NAME\"");
			remoteTableColumns[63].ShouldBe("\"INMEMORY_DUPLICATE\"");
		}

		[Test]
		public void TestTableSpaceAllocationDataProvider()
		{
			var model = new TableDetailsModel();
			var tableSpaceAllocationDataProvider = new TableSpaceAllocationDataProvider(model, new OracleObjectIdentifier(OracleDatabaseModelBase.SchemaSys, "\"DUAL\""));

			ExecuteDataProvider(tableSpaceAllocationDataProvider);

			model.AllocatedBytes.ShouldBe(65536);
		}

		[Test]
		public void TestDisplayCursorDataProvider()
		{
			var displayCursorDataProvider = DisplayCursorDataProvider.CreateDisplayLastCursorDataProvider();
			ExecuteDataProvider(displayCursorDataProvider);

			displayCursorDataProvider.PlanText.ShouldNotBe(null);
			Trace.WriteLine(displayCursorDataProvider.PlanText);
		}

		[Test]
		public void TestExplainPlanDataProvider()
		{
			Task<ExecutionPlanItemCollection> task;
			using (var databaseModel = OracleDatabaseModel.GetDatabaseModel(_connectionString))
			{
				var executionModel =
					new StatementExecutionModel
					{
						BindVariables = new BindVariableModel[0],
						StatementText = ExplainPlanTestQuery
					};

				task = databaseModel.ExplainPlanAsync(executionModel, CancellationToken.None);
				task.Wait();
			}

			var rootItem = task.Result.RootItem;
			task.Result.AllItems.Count.ShouldBe(12);
			rootItem.ShouldNotBe(null);
			rootItem.Operation.ShouldBe("SELECT STATEMENT");
			rootItem.ExecutionOrder.ShouldBe(12);
			rootItem.ChildItems.Count.ShouldBe(1);
			rootItem.ChildItems[0].ExecutionOrder.ShouldBe(11);
			rootItem.ChildItems[0].Operation.ShouldBe("HASH JOIN");
			rootItem.ChildItems[0].ChildItems.Count.ShouldBe(2);
			rootItem.ChildItems[0].ChildItems[0].ExecutionOrder.ShouldBe(7);
			rootItem.ChildItems[0].ChildItems[0].Operation.ShouldBe("MERGE JOIN");
			rootItem.ChildItems[0].ChildItems[0].ChildItems.Count.ShouldBe(2);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[0].ExecutionOrder.ShouldBe(3);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[0].Operation.ShouldBe("VIEW");
			rootItem.ChildItems[0].ChildItems[0].ChildItems[0].ChildItems.Count.ShouldBe(1);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[0].ChildItems[0].ExecutionOrder.ShouldBe(2);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[0].ChildItems[0].Operation.ShouldBe("COUNT");
			rootItem.ChildItems[0].ChildItems[0].ChildItems[0].ChildItems[0].ChildItems.Count.ShouldBe(1);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[0].ChildItems[0].ChildItems[0].ExecutionOrder.ShouldBe(1);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[0].ChildItems[0].ChildItems[0].Operation.ShouldBe("FAST DUAL");
			rootItem.ChildItems[0].ChildItems[0].ChildItems[0].ChildItems[0].ChildItems[0].ChildItems.Count.ShouldBe(0);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[1].ExecutionOrder.ShouldBe(6);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[1].Operation.ShouldBe("VIEW");
			rootItem.ChildItems[0].ChildItems[0].ChildItems[1].ChildItems.Count.ShouldBe(1);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[1].ChildItems[0].ExecutionOrder.ShouldBe(5);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[1].ChildItems[0].Operation.ShouldBe("COUNT");
			rootItem.ChildItems[0].ChildItems[0].ChildItems[1].ChildItems[0].ChildItems.Count.ShouldBe(1);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[1].ChildItems[0].ChildItems[0].ExecutionOrder.ShouldBe(4);
			rootItem.ChildItems[0].ChildItems[0].ChildItems[1].ChildItems[0].ChildItems[0].Operation.ShouldBe("FAST DUAL");
			rootItem.ChildItems[0].ChildItems[0].ChildItems[1].ChildItems[0].ChildItems[0].ChildItems.Count.ShouldBe(0);
			rootItem.ChildItems[0].ChildItems[1].ExecutionOrder.ShouldBe(10);
			rootItem.ChildItems[0].ChildItems[1].Operation.ShouldBe("VIEW");
			rootItem.ChildItems[0].ChildItems[1].ChildItems.Count.ShouldBe(1);
			rootItem.ChildItems[0].ChildItems[1].ChildItems[0].ExecutionOrder.ShouldBe(9);
			rootItem.ChildItems[0].ChildItems[1].ChildItems[0].Operation.ShouldBe("COUNT");
			rootItem.ChildItems[0].ChildItems[1].ChildItems[0].ChildItems.Count.ShouldBe(1);
			rootItem.ChildItems[0].ChildItems[1].ChildItems[0].ChildItems[0].ExecutionOrder.ShouldBe(8);
			rootItem.ChildItems[0].ChildItems[1].ChildItems[0].ChildItems[0].Operation.ShouldBe("FAST DUAL");
			rootItem.ChildItems[0].ChildItems[1].ChildItems[0].ChildItems[0].ChildItems.Count.ShouldBe(0);
		}

		[Test]
		public void TestCursorExecutionStatisticsDataProvider()
		{
			Task<ExecutionStatisticsPlanItemCollection> task;
			using (var databaseModel = OracleDatabaseModel.GetDatabaseModel(_connectionString))
			{
				var executionModel =
					new StatementExecutionModel
					{
						BindVariables = new BindVariableModel[0],
						StatementText = ExplainPlanTestQuery
					};

				databaseModel.ExecuteStatement(executionModel);

				task = databaseModel.GetCursorExecutionStatisticsAsync(CancellationToken.None);
				task.Wait();
			}

			var allItems = task.Result.AllItems.Values.ToArray();
			allItems.Length.ShouldBe(12);

			var hashJoin = allItems[1];
			hashJoin.Operation.ShouldBe("HASH JOIN");
			hashJoin.Executions.ShouldBeGreaterThan(0);
			hashJoin.LastStarts.ShouldNotBe(null);
			hashJoin.TotalStarts.ShouldNotBe(null);
			hashJoin.LastOutputRows.ShouldNotBe(null);
			hashJoin.TotalOutputRows.ShouldNotBe(null);
			hashJoin.LastConsistentReadBufferGets.ShouldNotBe(null);
			hashJoin.TotalConsistentReadBufferGets.ShouldNotBe(null);
			hashJoin.LastCurrentReadBufferGets.ShouldNotBe(null);
			hashJoin.TotalCurrentReadBufferGets.ShouldNotBe(null);
			hashJoin.LastDiskReads.ShouldNotBe(null);
			hashJoin.TotalDiskReads.ShouldNotBe(null);
			hashJoin.LastDiskWrites.ShouldNotBe(null);
			hashJoin.TotalDiskWrites.ShouldNotBe(null);
			hashJoin.LastElapsedTime.ShouldNotBe(null);
			hashJoin.TotalElapsedTime.ShouldNotBe(null);
			hashJoin.WorkAreaSizingPolicy.ShouldNotBe(null);
			hashJoin.EstimatedOptimalSizeBytes.ShouldNotBe(null);
			hashJoin.EstimatedOnePassSizeBytes.ShouldNotBe(null);
			hashJoin.LastMemoryUsedBytes.ShouldNotBe(null);
			hashJoin.LastExecutionMethod.ShouldNotBe(null);
			hashJoin.LastParallelDegree.ShouldNotBe(null);
			hashJoin.TotalWorkAreaExecutions.ShouldNotBe(null);
			hashJoin.OptimalWorkAreaExecutions.ShouldNotBe(null);
			hashJoin.OnePassWorkAreaExecutions.ShouldNotBe(null);
			hashJoin.MultiPassWorkAreaExecutions.ShouldNotBe(null);
			hashJoin.ActiveWorkAreaTime.ShouldNotBe(null);
		}

		[Test]
		public void TestComplextExplainExecutionOrder()
		{
			const string testQuery =
@"WITH PLAN_SOURCE AS (
	SELECT
		OPERATION, OPTIONS, ID, PARENT_ID, DEPTH, CASE WHEN PARENT_ID IS NULL THEN 1 ELSE POSITION END POSITION
	FROM V$SQL_PLAN
	WHERE SQL_ID = :SQL_ID AND CHILD_NUMBER = :CHILD_NUMBER)
SELECT
	OPERATION, OPTIONS,
	ID,
	DEPTH, POSITION, CHILDREN_COUNT, TMP, TREEPATH, TREEPATH_SUM
FROM (
	SELECT
		OPERATION,
		OPTIONS,
		ID,
		DEPTH,
		POSITION,
		CHILDREN_COUNT,
		(SELECT NVL(SUM(CHILDREN_COUNT), 0) FROM PLAN_SOURCE CHILDREN WHERE PARENT_ID = PLAN_DATA.PARENT_ID AND POSITION < PLAN_DATA.POSITION) TMP,
		TREEPATH,
		(SELECT
			SUM(
				SUBSTR(
			        TREEPATH,
			        INSTR(TREEPATH, '|', 1, LEVEL) + 1,
			        INSTR(TREEPATH, '|', 1, LEVEL + 1) - INSTR(TREEPATH, '|', 1, LEVEL) - 1
			    )
		    ) AS TOKEN
		FROM
			DUAL
		CONNECT BY
			LEVEL <= LENGTH(TREEPATH) - LENGTH(REPLACE(TREEPATH, '|', NULL))
		) TREEPATH_SUM
	FROM (
		SELECT
			LPAD('-', 2 * DEPTH, '-') || OPERATION OPERATION,
			SYS_CONNECT_BY_PATH(POSITION, '|') || '|' TREEPATH,
			OPTIONS,
			ID, PARENT_ID, DEPTH, POSITION,
			(SELECT COUNT(*) FROM PLAN_SOURCE CHILDREN START WITH ID = PLAN_SOURCE.ID CONNECT BY PRIOR ID = PARENT_ID) CHILDREN_COUNT
		FROM
			PLAN_SOURCE
		START WITH
			PARENT_ID IS NULL
		CONNECT BY
			PARENT_ID = PRIOR ID		
	) PLAN_DATA
) RICH_PLAN_DATA
ORDER BY
	ID";
			
			Task<ExecutionPlanItemCollection> task;
			using (var databaseModel = OracleDatabaseModel.GetDatabaseModel(_connectionString))
			{
				var executionModel =
					new StatementExecutionModel
					{
						BindVariables = new BindVariableModel[0],
						StatementText = testQuery
					};

				task = databaseModel.ExplainPlanAsync(executionModel, CancellationToken.None);
				task.Wait();
			}

			var executionOrder = task.Result.AllItems.Values.Select(i => i.ExecutionOrder).ToArray();
			var expectedExecutionOrder = new [] { 19, 4, 3, 2, 1, 7, 6, 5, 10, 9, 8, 18, 12, 11, 17, 16, 15, 14, 13 };

			executionOrder.ShouldBe(expectedExecutionOrder);
		}

		private void ExecuteDataProvider(params IModelDataProvider[] updaters)
		{
			using (var databaseModel = OracleDatabaseModel.GetDatabaseModel(_connectionString))
			{
				var task = (Task)typeof (OracleDatabaseModel).GetMethod("UpdateModelAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(databaseModel, new object[] { CancellationToken.None, false, updaters });
				task.Wait();
			}
		}
	}
}
