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
/// Quick notes (ADR 0023): "Add a note" box (Enter adds), the latest notes (Enter, F2 or double-click edits; Shift+Enter
/// adds a line while editing), a delete button per note. Rows are reused by id.
/// </summary>
internal sealed class NotesView : WidgetView
{
    private readonly WidgetPresenter _presenter;
    private readonly StackPanel _panel = new() { Margin = new Thickness(0, 6, 0, 2) };
    private readonly StackPanel _rows = new();
    private readonly TextBlock _footer = PersonalDataParts.MessageLine();
    private readonly Dictionary<long, Row> _byId = [];
    private readonly TextBox _add;

    public NotesView(WidgetPresenter presenter)
    {
        _presenter = presenter;
        (var field, _add) = PersonalDataParts.AddBox("Add a note", "Notes.Add", text => Send(PersonalDataWidget.AddCommand + text));
        _panel.Children.Add(field);
        _panel.Children.Add(_rows);
        _panel.Children.Add(_footer);
        AutomationProperties.SetName(_panel, "Notes");
    }

    public override UIElement Element => _panel;

    public override void Update(object snapshot)
    {
        var notes = (NotesSnapshot)snapshot;
        _add.IsEnabled = !notes.ReadOnly;

        var wanted = new List<UIElement>(notes.Items.Count);
        foreach (var note in notes.Items)
        {
            if (!_byId.TryGetValue(note.Id, out var row))
            {
                row = new Row(this, note.Id);
                _byId.Add(note.Id, row);
            }

            row.Update(note, notes.ReadOnly);
            wanted.Add(row.Root);
        }

        foreach (var gone in _byId.Keys.Except(notes.Items.Select(n => n.Id)).ToArray())
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
        }

        var more = notes.Total - notes.Items.Count;
        var message = PersonalDataParts.Message(notes.LastResult, notes.ReadOnly, PersonalDataRules.MaxNoteLength);
        _footer.Text = message.Length > 0 ? message
            : more > 0 ? string.Create(CultureInfo.CurrentCulture, $"+{more} older notes")
            : notes.Items.Count == 0 ? "No notes yet." : string.Empty;
        _footer.Visibility = _footer.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Send(string command) => _presenter.Execute(WidgetIds.Notes, command);

    private sealed class Row
    {
        private readonly NotesView _owner;
        private readonly long _id;
        private readonly Button _open;
        private readonly TextBlock _text = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 36 }
            .Themed(TextBlock.ForegroundProperty, ThemeResources.TextPrimary);
        private readonly Button _delete;
        private Note? _note;
        private bool _editing;

        public Row(NotesView owner, long id)
        {
            _owner = owner;
            _id = id;
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // A flat button holding the note: Enter, Space or a click opens the editor; keyboard reachable.
            _open = new Button
            {
                Content = _text,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(2),
                Margin = new Thickness(0, 1, 0, 1),
                Style = MediaView.IconButtonStyle,
            };
            _open.Click += (_, _) => BeginEdit();
            _open.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.F2)
                {
                    BeginEdit();
                    e.Handled = true;
                }
            };
            Root.Children.Add(_open);

            _delete = PersonalDataParts.DeleteButton("note", () => owner.Send(PersonalDataWidget.Delete(id)));
            Grid.SetColumn(_delete, 1);
            Root.Children.Add(_delete);
        }

        public Grid Root { get; } = new();

        public void Update(Note note, bool readOnly)
        {
            _note = note;
            _open.IsEnabled = !readOnly;
            _delete.IsEnabled = !readOnly;
            if (_editing)
            {
                return; // Never overwrite what the user is typing.
            }

            _text.Text = note.Text;
            AutomationProperties.SetName(_open, note.Text);
            AutomationProperties.SetHelpText(_open, "Enter edits");
        }

        private void BeginEdit()
        {
            if (_note is null || !_open.IsEnabled)
            {
                return;
            }

            _editing = true;
            var editor = PersonalDataParts.Editor(_note.Text, multiLine: true, PersonalDataRules.MaxNoteLength, text =>
            {
                _owner.Send(PersonalDataWidget.Edit(_id, text));
                EndEdit();
            }, EndEdit);
            Root.Children.Remove(_open);
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
            Root.Children.Insert(0, _open);
            if (_note is not null)
            {
                Update(_note, !_open.IsEnabled);
            }

            _open.Focus();
        }
    }
}
