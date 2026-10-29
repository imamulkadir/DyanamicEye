using System.Globalization;
using System.Text;

namespace DynamicIsland.Core.Diagnostics;

/// <summary>
/// Describes an exception for the local logs without its messages (P07-01 threat model). Messages can carry file names,
/// paths outside the profile or text from system APIs that the redactor cannot recognise; the type, HRESULT and stack
/// trace are enough to diagnose a failure and hold only code locations.
/// </summary>
public static class ExceptionText
{
    private const int MaxDepth = 8;

    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = new StringBuilder();
        Append(text, exception, 0);
        return text.ToString();
    }

    private static void Append(StringBuilder text, Exception exception, int depth)
    {
        if (depth > 0)
        {
            text.Append(" ---> ");
        }

        text.Append(exception.GetType().FullName)
            .Append(" (HResult 0x")
            .Append(exception.HResult.ToString("X8", CultureInfo.InvariantCulture))
            .Append(')');
        if (exception.StackTrace is { } stack)
        {
            text.Append('\n').Append(stack);
        }

        text.Append('\n');
        if (depth >= MaxDepth)
        {
            return;
        }

        var inner = exception is AggregateException aggregate ? aggregate.InnerExceptions : (IEnumerable<Exception?>)[exception.InnerException];
        foreach (var child in inner)
        {
            if (child is not null)
            {
                Append(text, child, depth + 1);
            }
        }
    }
}
