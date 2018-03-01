using System;

namespace GZipTest
{
    /// <summary>
    /// Параметры события прогресса
    /// </summary>
    internal class ProgressEventArgs : EventArgs
    {
        public int Progress { get; }

        public ProgressEventArgs(int progress)
        {
            Progress = progress;
        }
    }
}