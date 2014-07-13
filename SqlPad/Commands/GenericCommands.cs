using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace SqlPad.Commands
{
	public static class GenericCommands
	{
		public static RoutedCommand ShowFunctionOverloadCommand = new RoutedCommand("ShowFunctionOverloads", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.Space, ModifierKeys.Control | ModifierKeys.Shift) });
		public static RoutedCommand DuplicateTextCommand = new RoutedCommand("DuplicateText", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.D, ModifierKeys.Control) });
		public static RoutedCommand BlockCommentCommand = new RoutedCommand("BlockComment", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.Oem2, ModifierKeys.Control | ModifierKeys.Shift) });
		public static RoutedCommand LineCommentCommand = new RoutedCommand("LineComment", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.Oem2, ModifierKeys.Control | ModifierKeys.Alt) });
		public static RoutedCommand ListContextActionCommand = new RoutedCommand("ListContextActions", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.Enter, ModifierKeys.Alt) });
		public static RoutedCommand MultiNodeEditCommand = new RoutedCommand("EditMultipleNodes", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.F6, ModifierKeys.Shift) });
		public static RoutedCommand NavigateToPreviousUsageCommand = new RoutedCommand("NavigateToPreviousHighlightedUsage", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.PageUp, ModifierKeys.Control | ModifierKeys.Alt) });
		public static RoutedCommand NavigateToNextUsageCommand = new RoutedCommand("NavigateToNextHighlightedUsage", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.PageDown, ModifierKeys.Control | ModifierKeys.Alt) });
		public static RoutedCommand NavigateToQueryBlockRootCommand = new RoutedCommand("NavigateToQueryBlockRoot", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.Home, ModifierKeys.Control | ModifierKeys.Alt) });
		public static RoutedCommand NavigateToDefinitionRootCommand = new RoutedCommand("NavigateToDefinition", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.F12) });
		public static RoutedCommand ExecuteDatabaseCommandCommand = new RoutedCommand("ExecuteDatabaseCommand", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.F9), new KeyGesture(Key.Enter, ModifierKeys.Control) });
		public static RoutedCommand SaveCommand = new RoutedCommand("Save", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.S, ModifierKeys.Control) });
		public static RoutedCommand FormatStatementCommand = new RoutedCommand("FormatStatement", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.F, ModifierKeys.Control | ModifierKeys.Alt) });
		public static RoutedCommand FindUsagesCommand = new RoutedCommand("FindUsages", typeof(TextEditor), new InputGestureCollection { new KeyGesture(Key.F11, ModifierKeys.Alt | ModifierKeys.Shift) });
		public static RoutedCommand FetchNextRowsCommand = new RoutedCommand("FetchNextRows", typeof(DataGrid), new InputGestureCollection { new KeyGesture(Key.PageDown), new KeyGesture(Key.Down) });
	}
}