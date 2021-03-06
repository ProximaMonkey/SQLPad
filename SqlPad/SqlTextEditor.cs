﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Search;
using SqlPad.Commands;

namespace SqlPad
{
	public class SqlTextEditor : TextEditor
	{
		public static readonly DependencyPropertyKey CurrentLineKey = DependencyProperty.RegisterReadOnly(nameof(CurrentLine), typeof(int), typeof(SqlTextEditor), new FrameworkPropertyMetadata(0));
		public static readonly DependencyPropertyKey CurrentColumnKey = DependencyProperty.RegisterReadOnly(nameof(CurrentColumn), typeof(int), typeof(SqlTextEditor), new FrameworkPropertyMetadata(0));
		public static readonly DependencyPropertyKey CurrentSelectionLengthKey = DependencyProperty.RegisterReadOnly(nameof(CurrentSelectionLength), typeof(int?), typeof(SqlTextEditor), new FrameworkPropertyMetadata(null));

		private const double FontSizeMin = 8;
		private const double FontSizeMax = 72;

		private static readonly double[] FontSizes = { FontSizeMin, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, FontSizeMax };

		public int CurrentLine => (int)GetValue(CurrentLineKey.DependencyProperty);

		public int CurrentColumn => (int)GetValue(CurrentColumnKey.DependencyProperty);

		public int? CurrentSelectionLength => (int?)GetValue(CurrentSelectionLengthKey.DependencyProperty);

		public bool IsMultiSelectionActive => TextArea.Selection.Segments.Skip(1).Any();

		protected override void OnInitialized(EventArgs e)
		{
			Options.AllowScrollBelowDocument = true;
			Options.AllowToggleOverstrikeMode = true;
			Options.IndentationSize = ConfigurationProvider.Configuration.Editor.IndentationSize;

			base.OnInitialized(e);

			TextArea.Caret.PositionChanged += CaretPositionChangedHandler;
			TextArea.SelectionChanged += TextAreaSelectionChangedHandler;
			TextArea.PreviewMouseWheel += PreviewMouseWheelHandler;

			var searchPanel = SearchPanel.Install(this);
			searchPanel.Style = (Style)Application.Current.Resources["SearchPanelStyle"];

			ChangeDeleteLineCommandInputGesture();

			TextArea.CommandBindings.Add(new CommandBinding(GenericCommands.DuplicateText, GenericCommandHandler.DuplicateText, CanExecuteEditCommand));
			TextArea.CommandBindings.Add(new CommandBinding(GenericCommands.BlockComment, GenericCommandHandler.HandleBlockComments, CanExecuteEditCommand));
			TextArea.CommandBindings.Add(new CommandBinding(GenericCommands.LineComment, GenericCommandHandler.HandleLineComments, CanExecuteEditCommand));
			TextArea.CommandBindings.Add(new CommandBinding(GenericCommands.DeleteAndCopyLine, DeleteAndCopyLineHandler, CanExecuteEditCommand));
		}

		private void DeleteAndCopyLineHandler(object sender, ExecutedRoutedEventArgs e)
		{
			var line = Document.GetLineByNumber(CurrentLine);
			Clipboard.SetText(Document.GetText(line));
			AvalonEditCommands.DeleteLine.Execute(null, null);
        }

		private void CanExecuteEditCommand(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = !IsReadOnly;
		}

		private void ChangeDeleteLineCommandInputGesture()
		{
			var deleteLineCommand = (RoutedCommand)TextArea.DefaultInputHandler.Editing.CommandBindings
				.Single(b => b.Command == AvalonEditCommands.DeleteLine)
				.Command;

			deleteLineCommand.InputGestures[0] = new KeyGesture(Key.L, ModifierKeys.Control | ModifierKeys.Shift);
		}

		private void TextAreaSelectionChangedHandler(object sender, EventArgs eventArgs)
		{
			SetValue(CurrentSelectionLengthKey, SelectionLength == 0 ? null : (int?)SelectionLength);
		}

		private void CaretPositionChangedHandler(object sender, EventArgs eventArgs)
		{
			var location = Document.GetLocation(CaretOffset);

			SetValue(CurrentLineKey, location.Line);
			SetValue(CurrentColumnKey, location.Column);
		}

		public void RedrawSegments(IEnumerable<SourcePosition> segments)
		{
			foreach (var segment in segments)
			{
				TextArea.TextView.Redraw(segment.IndexStart, segment.Length);
			}
		}

		public bool TryRemoveBlockComment()
		{
			const int commentHeadingLength = 2;
			var commentRemoved = false;

			if (SelectionLength == 0)
			{
				if (Text.Length - CaretOffset >= commentHeadingLength && CaretOffset >= commentHeadingLength &&
					Text.Substring(CaretOffset - commentHeadingLength, 4) == "/**/")
				{
					commentRemoved = true;
					Document.Remove(CaretOffset - commentHeadingLength, 4);
				}

				if (Text.Length - CaretOffset >= 2 * commentHeadingLength &&
					Text.Substring(CaretOffset, 4) == "/**/")
				{
					commentRemoved = true;
					Document.Remove(CaretOffset, 4);
				}
			}
			else if (Text.Length - SelectionStart >= commentHeadingLength && SelectionStart + SelectionLength > commentHeadingLength &&
					 Text.Substring(SelectionStart, commentHeadingLength) == "/*" &&
					 Text.Substring(SelectionStart + SelectionLength - commentHeadingLength, commentHeadingLength) == "*/")
			{
				commentRemoved = true;
				Document.Remove(SelectionStart + SelectionLength - commentHeadingLength, commentHeadingLength);
				Document.Remove(SelectionStart, commentHeadingLength);
			}

			return commentRemoved;
		}

		public void ReplaceTextSegments(ICollection<TextSegment> textSegments)
		{
			Document.BeginUpdate();

			foreach (var textSegment in textSegments.OrderByDescending(s => s.IndextStart).ThenByDescending(s => s.Length))
			{
				Document.Replace(textSegment.IndextStart, textSegment.Length, textSegment.Text);
			}

			Document.EndUpdate();
		}

		public void NavigateToOffset(int? offset)
		{
			if (offset == null)
			{
				return;
			}

			CaretOffset = offset.Value;
			ScrollToCaret();
		}

		public void ScrollToCaret()
		{
			var location = Document.GetLocation(CaretOffset);
			ScrollTo(location.Line, location.Column);
		}

		public int GetLineNumberByOffset(int offset)
		{
			return Document.GetLineByOffset(offset).LineNumber;
		}

		public void ZoomIn()
		{
			if (FontSize < FontSizeMax)
			{
				FontSize = FontSizes.First(s => s > FontSize);
			}
		}

		public void ZoomOut()
		{
			if (FontSize > FontSizeMin)
			{
				FontSize = FontSizes.Reverse().First(s => s < FontSize);
			}
		}

		private void PreviewMouseWheelHandler(object sender, MouseWheelEventArgs e)
		{
			if (Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0)
			{
				return;
			}

			if (e.Delta > 0 && FontSize < FontSizeMax)
			{
				ZoomIn();
			}
			else if (e.Delta < 0 && FontSize > FontSizeMin)
			{
				ZoomOut();
			}

			e.Handled = true;
		}
	}
}
