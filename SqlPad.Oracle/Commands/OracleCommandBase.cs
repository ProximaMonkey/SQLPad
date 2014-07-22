﻿using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SqlPad.Commands;

namespace SqlPad.Oracle.Commands
{
	internal abstract class OracleCommandBase
	{
		protected readonly CommandExecutionContext ExecutionContext;

		protected StatementGrammarNode CurrentNode { get; private set; }
		
		protected OracleStatementSemanticModel SemanticModel { get; private set; }

		protected OracleQueryBlock CurrentQueryBlock { get; private set; }

		protected virtual Func<StatementGrammarNode, bool> CurrentNodeFilterFunction { get { return null; } }

		protected OracleCommandBase(CommandExecutionContext executionContext)
		{
			if (executionContext == null)
				throw new ArgumentNullException("executionContext");

			ExecutionContext = executionContext;

			CurrentNode = executionContext.DocumentRepository.Statements.GetNodeAtPosition(executionContext.CaretOffset, CurrentNodeFilterFunction);

			if (CurrentNode == null)
				return;

			SemanticModel = (OracleStatementSemanticModel)executionContext.DocumentRepository.ValidationModels[CurrentNode.Statement].SemanticModel;
			CurrentQueryBlock = SemanticModel.GetQueryBlock(CurrentNode);
		}

		protected virtual bool CanExecute()
		{
			return true;
		}

		protected abstract void Execute();

		protected virtual Task ExecuteAsync(CancellationToken cancellationToken)
		{
			throw new NotImplementedException();
		}

		public static CommandExecutionHandler CreateStandardExecutionHandler<TCommand>(string commandName) where TCommand : OracleCommandBase
		{
			return new CommandExecutionHandler
			{
				Name = commandName,
				CanExecuteHandler = context => CreateCommandInstance<TCommand>(context).CanExecute(),
				ExecutionHandler = CreateExecutionHandler<TCommand>(),
				ExecutionHandlerAsync = CreateAsynchronousExecutionHandler<TCommand>()
			};			
		}

		private static Action<CommandExecutionContext> CreateExecutionHandler<TCommand>() where TCommand : OracleCommandBase
		{
			return context =>
			       {
					   var commandInstance = CreateCommandInstance<TCommand>(context);
				       if (commandInstance.CanExecute())
				       {
					       commandInstance.Execute();
				       }
			       };
		}

		private static Func<CommandExecutionContext, CancellationToken, Task> CreateAsynchronousExecutionHandler<TCommand>() where TCommand : OracleCommandBase
		{
			return (context, cancellationToken) =>
			{
				var commandInstance = CreateCommandInstance<TCommand>(context);

				Task task;
				if (commandInstance.CanExecute())
				{
					task = commandInstance.ExecuteAsync(cancellationToken);
				}
				else
				{
					var source = new TaskCompletionSource<object>();
					source.SetResult(null);
					task = source.Task;
				}

				return task;
			};
		}

		private static TCommand CreateCommandInstance<TCommand>(CommandExecutionContext executionContext)
		{
			var constructorInfo = typeof(TCommand).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
			return (TCommand)constructorInfo.Invoke(new object[] { executionContext });
		}
	}
}
