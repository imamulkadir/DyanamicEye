using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Core.PersonalData;
using DynamicIsland.Core.Settings;
using DynamicIsland.Widgets;

namespace DynamicIsland.App;

/// <summary>
/// Task list (ADR 0023): "Add a task" box (Enter adds), check boxes (Space toggles), F2 or double-click edits, a delete
/// button per task. Rows are reused by id, so focus stays on a task when it moves after being completed.
/// </summary>
internal sealed class TasksView : WidgetView
{
    private readonly WidgetPresenter _presenter;
    private readonly StackPanel _panel = new() { Margin = new Thickness(0, 6, 0, 2) };
    private readonly StackPanel _rows = new();
    private readonly TextBlock _footer = PersonalDataParts.MessageLine();
    private readonly Dictionary<long, Row> _byId = [];
    private readonly TextBox _add;

    public TasksView(WidgetPresenter presenter)
    {
        _presenter = presenter;
        (var field, _add) = PersonalDataParts.AddBox("Add a task", "Tasks.Add", text => Send(PersonalDataWidget.AddCommand + text));
        _add.MaxLength = PersonalDataRules.MaxTaskLength;
        _panel.Children.Add(field);
        _panel.Children.Add(_rows);
        _panel.Children.Add(_footer);
        AutomationProperties.SetName(_panel, "Tasks");
    }

    public override UIElement Element => _panel;

    public override void Update(object snapshot)
    {
        var tasks = (TasksSnapshot)snapshot;
        _add.IsEnabled = !tasks.ReadOnly;

        var focusedId = _byId.FirstOrDefault(p => p.Value.Root.IsKeyboardFocusWithin).Key;
        var wanted = new List<UIElement>(tasks.Items.Count);
        foreach (var task in tasks.Items)
        {
            if (!_byId.TryGetValue(task.Id, out var row))
            {
                row = new Row(this, task.Id);
                _byId.Add(task.Id, row);
            }

            row.Update(task, tasks.ReadOnly);
            wanted.Add(row.Root);
        }

        foreach (var gone in _byId.Keys.Except(tasks.Items.Select(t => t.Id)).ToArray())
        {
            _byId.Remove(gone);
        }

        if (!wanted.SequenceEqual(_rows.Children.OfType<UIElement>()))
        {
            _rows.Children.Clear();
            foreach (var element in wanted)
            {
                _rows.Children.Add(element);
            }

            // A completed task moves down; keep the keyboard on it.
            if (focusedId != 0 && _byId.TryGetValue(focusedId, out var focused))
            {
                focused.Focus();
            }
        }

        var shown = tasks.Items.Count;
        var more = tasks.OpenCount + tasks.DoneCount - shown;
        var message = PersonalDataParts.Message(tasks.LastResult, tasks.ReadOnly, PersonalDataRules.MaxTaskLength);
        _footer.Text = message.Length > 0 ? message
            : more > 0 ? string.Create(CultureInfo.CurrentCulture, $"+{more} more")
            : shown == 0 ? "No tasks yet." : string.Empty;
        _footer.Visibility = _footer.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Send(string command) => _presenter.Execute(WidgetIds.Tasks, command);

    private sealed class Row
    {
        private readonly TasksView _owner;
        private readonly long _id;
        private readonly CheckBox _check;
        private readonly TextBlock _text = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }
            .Themed(TextBlock.ForegroundProperty, ThemeResources.TextPrimary);
        private readonly Button _delete;
        private TaskItem? _task;
        private bool _editing;

        public Row(TasksView owner, long id)
        {
            _owner = owner;
            _id = id;
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _check = new CheckBox { Content = _text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) }
                .Themed(Control.ForegroundProperty, ThemeResources.TextPrimary);
            _check.Click += (_, _) => owner.Send(PersonalDataWidget.Toggle(id));
            _check.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.F2)
                {
                    BeginEdit();
                    e.Handled = true;
                }
            };
            _text.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    BeginEdit();
                    e.Handled = true;
                }
            };
            Root.Children.Add(_check);

            _delete = PersonalDataParts.DeleteButton("task", () => owner.Send(PersonalDataWidget.Delete(id)));
            Grid.SetColumn(_delete, 1);
            Root.Children.Add(_delete);
        }

        public Grid Root { get; } = new();

        public void Update(TaskItem task, bool readOnly)
        {
            _task = task;
            _check.IsEnabled = !readOnly;
            _delete.IsEnabled = !readOnly;
            if (_editing)
            {
                return; // Never overwrite what the user is typing.
            }

            _text.Text = task.Text;
            _text.TextDecorations = task.Done ? TextDecorations.Strikethrough : null;
            _text.Opacity = task.Done ? 0.6 : 1;
            _check.IsChecked = task.Done;
            AutomationProperties.SetName(_check, task.Text);
            AutomationProperties.SetHelpText(_check, "Space completes; F2 edits");
        }

        public void Focus() => _check.Focus();

        private void BeginEdit()
        {
            if (_task is null || !_check.IsEnabled)
            {
                return;
            }

            _editing = true;
            var editor = PersonalDataParts.Editor(_task.Text, multiLine: false, PersonalDataRules.MaxTaskLength, text =>
            {
                _owner.Send(PersonalDataWidget.Edit(_id, text));
                EndEdit();
            }, EndEdit);
            Root.Children.Remove(_check);
            Root.Children.Insert(0, editor);
        }

        private void EndEdit()
        {
            if (!_editing)
            {
                return;
            }

            _editing = false;
            Root.Children.RemoveAt(0);
            Root.Children.Insert(0, _check);
            if (_task is not null)
            {
                Update(_task, !_check.IsEnabled);
            }

            _check.Focus();
        }
    }
}
