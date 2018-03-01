using System;

namespace GZipTest
{
    /// <summary>
    /// Исключение при некорректных входных параметрах
    /// </summary>
    internal class BadParamsException : Exception
    {
        public BadParamsException(string message): base(message) {}
    }
}
