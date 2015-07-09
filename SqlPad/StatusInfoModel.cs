using System.Windows;

namespace SqlPad
{
	public class StatusInfoModel : ModelBase
	{
		private int _selectedRowIndex;
		private int _affectedRowCount = -1;
		private string _executionTimerMessage;
		private bool _moreRowsAvailable;
		private bool _resultGridAvailable;
		private bool _ddlStatementExecutedSuccessfully;

		public int SelectedRowIndex
		{
			get { return _selectedRowIndex; }
			set { UpdateValueAndRaisePropertyChanged(ref _selectedRowIndex, value); }
		}

		public bool ResultGridAvailable
		{
			get { return _resultGridAvailable; }
			set
			{
				if (UpdateValueAndRaisePropertyChanged(ref _resultGridAvailable, value))
				{
					RaisePropertyChanged("StatementExecutionInfoSeparatorVisibility");
				}
			}
		}

		public bool DdlStatementExecutedSuccessfully
		{
			get { return _ddlStatementExecutedSuccessfully; }
			set
			{
				if (UpdateValueAndRaisePropertyChanged(ref _ddlStatementExecutedSuccessfully, value))
				{
					RaisePropertyChanged("StatementExecutionInfoSeparatorVisibility");
				}
			}
		}

		public bool MoreRowsAvailable
		{
			get { return _moreRowsAvailable; }
			set { UpdateValueAndRaisePropertyChanged(ref _moreRowsAvailable, value); }
		}

		public string ExecutionTimerMessage
		{
			get { return _executionTimerMessage; }
			set { UpdateValueAndRaisePropertyChanged(ref _executionTimerMessage, value); }
		}

		public int AffectedRowCount
		{
			get { return _affectedRowCount; }
			set
			{
				if (!UpdateValueAndRaisePropertyChanged(ref _affectedRowCount, value))
					return;

				RaisePropertyChanged("AffectedRowCountVisibility");
				RaisePropertyChanged("StatementExecutionInfoSeparatorVisibility");
			}
		}

		public Visibility AffectedRowCountVisibility
		{
			get { return _affectedRowCount == -1 ? Visibility.Collapsed : Visibility.Visible; }
		}

		public Visibility StatementExecutionInfoSeparatorVisibility
		{
			get { return !_resultGridAvailable && (AffectedRowCountVisibility == Visibility.Collapsed && !_ddlStatementExecutedSuccessfully) ? Visibility.Collapsed : Visibility.Visible; }
		}
	}
}