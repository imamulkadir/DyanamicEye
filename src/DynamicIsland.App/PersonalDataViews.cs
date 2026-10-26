using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core.PersonalData;

namespace DynamicIsland.App;

/// <summary>Pieces shared by the notes and tasks views (ADR 0023): the add box, item buttons and result messages.</summary>
internal static class PersonalDataParts
{
    public static readonly Brush FieldBackground = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    /// <summary>
    /// A dark single-line text box with a placeholder drawn behind it; Enter submits, Esc clears. Returns the field to
    /// place in the layout and the text box inside it.
    /// </summary>
    public static (Grid Field, TextBox Box) AddBox(string placeholder, string automationId, Action<string> submit)
    {
        var hint = new TextBlock
        {
            Text = placeholder,
            Foreground = WidgetView.Secondary,
            FontSize = 13,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var box = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = WidgetView.Primary,
            CaretBrush = WidgetView.Primary,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 4, 4, 4),
            FontSize = 13,
            MaxLength = PersonalDataRules.MaxNoteLength,
        };
        AutomationProperties.SetName(box, placeholder);
        AutomationProperties.SetAutomationId(box, automationId);
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && box.Text.Trim().Length > 0)
            {
                submit(box.Text);
                box.Clear();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && box.Text.Length > 0)
            {
                box.Clear();
                e.Handled = true;
            }
        };

        var field = new Grid { Background = FieldBackground, Margin = new Thickness(0, 0, 0, 4) };
        field.Children.Add(hint);
        field.Children.Add(box);
        return (field, box);
    }

    public static Button DeleteButton(string itemName, Action delete)
    {
        var button = new Button
        {
            Content = "",
            FontFamily = new FontFamily(MediaView.IconFont),
            FontSize = 10,
            Width = 24,
            Height = 24,
            Style = MediaView.IconButtonStyle,
            ToolTip = "Delete",
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, "Delete " + itemName);
        button.Click += (_, _) => delete();
        return button;
    }

    /// <summary>A short message for a refused change; never contains the user's text.</summary>
    public static string Message(PersonalDataResult? result, bool readOnly, int maxLength) => readOnly
        ? "Read-only: this data was saved by a newer version of Dynamic Island."
        : result switch
        {
            PersonalDataResult.Invalid => $"Use 1–{maxLength} characters.",
            PersonalDataResult.LimitReached => "The list is full. Delete some items first.",
            PersonalDataResult.ReadOnly => "Read-only: this data was saved by a newer version of Dynamic Island.",
            _ => string.Empty,
        };

    public static TextBlock MessageLine() => new() { Foreground = WidgetView.Secondary, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };

    /// <summary>Inline editor (F2, double-click or Enter on a note): Enter saves (Shift+Enter: new line in notes), Esc or leaving cancels.</summary>
    public static TextBox Editor(string text, bool multiLine, int maxLength, Action<string> save, Action cancel)
    {
        var editor = new TextBox
        {
            Text = text,
            Background = FieldBackground,
            Foreground = WidgetView.Primary,
            CaretBrush = WidgetView.Primary,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 13,
            MaxLength = maxLength,
            AcceptsReturn = false,
            TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        AutomationProperties.SetName(editor, "Edit");
        var done = false;
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && multiLine && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                var caret = editor.CaretIndex;
                editor.Text = editor.Text.Insert(caret, "\n");
                editor.CaretIndex = caret + 1;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                done = true;
                save(editor.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                done = true;
                cancel();
                e.Handled = true;
            }
        };
        editor.LostKeyboardFocus += (_, _) =>
        {
            if (!done)
            {
                done = true;
                cancel();
            }
        };
        editor.Loaded += (_, _) =>
        {
            editor.Focus();
            editor.SelectAll();
        };
        return editor;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
