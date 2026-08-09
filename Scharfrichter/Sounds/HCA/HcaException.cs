using System;

namespace Scharfrichter.Codec.Sounds.HCA
{
    public class HcaException : Exception
    {
        public HcaException(string message)
            : base(message)
        {
            ActionError = ActionResult.Ok;
            ErrorMessage = message;
        }

        public HcaException(string message, ActionResult actionError)
            : base(message)
        {
            ActionError = actionError;
            ErrorMessage = message;
        }

        public HcaException(string message, ActionResult actionError, Exception innerException)
            : base(message, innerException)
        {
            ActionError = actionError;
            ErrorMessage = message;
        }

        public ActionResult ActionError { get; }
        public string ErrorMessage { get; }
    }

    internal static class ErrorMessages
    {
        public static string GetBufferTooSmall(int minimum, int actual)
        {
            return $"Buffer too small. Required minimum: {minimum}, actual: {actual}";
        }

        public static string GetInvalidParameter(string paramName)
        {
            return $"Parameter '{paramName}' is invalid.";
        }

        public static string GetChecksumNotMatch(int expected, int actual)
        {
            return $"Checksum does not match. Expected: {expected}({expected:x8}), actual: {actual}({actual:x8}).";
        }

        public static string GetMagicNotMatch(int expected, int actual)
        {
            return $"Magic does not match. Expected: {expected}({expected:x8}), actual: {actual}({actual:x8}).";
        }

        public static string GetAthInitializationFailed()
        {
            return "ATH table initialization failed.";
        }

        public static string GetCiphInitializationFailed()
        {
            return "CIPH table initialization failed.";
        }
    }
}