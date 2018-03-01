using System;
using System.Collections.Generic;

namespace GZipTest
{
    /// <summary>
    /// .net 3.5 - даже AggregateException нет. 
    /// Сделаем эрзац
    /// </summary>
    internal class AggregateException : Exception
    {
        public List<Exception> InnerExceptions { get; }
        
        public AggregateException(List<Exception> exceptions)
        {
            InnerExceptions = exceptions;
        }
    }
}