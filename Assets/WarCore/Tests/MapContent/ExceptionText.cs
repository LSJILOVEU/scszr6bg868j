using System;

namespace WarCore.Tests
{
    // Unity's Mono and .NET append ArgumentException.ParamName to Message in different forms; tests compare the .NET form.
    internal static class ExceptionText
    {
        internal static string Of(Exception e)
        {
            if (e is ArgumentException a && !string.IsNullOrEmpty(a.ParamName))
            {
                string suffix = new ArgumentException(string.Empty, a.ParamName).Message;
                if (a.Message.EndsWith(suffix, StringComparison.Ordinal))
                    return a.Message.Substring(0, a.Message.Length - suffix.Length) + " (Parameter '" + a.ParamName + "')";
            }
            return e.Message;
        }
    }
}
