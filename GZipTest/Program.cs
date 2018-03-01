using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GZipTest
{
    internal class Program
    {
        private const int ErrorExitCode = 1;

        static void Main(string[] args)
        {
            try
            {
                var parameters = ReadParameters(args);
                Work(parameters);
                return;
            }
            catch (BadParamsException ex)
            {
                Console.WriteLine(ex.Message);
                PrintHelp();
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(ex.Message);
            }
            Environment.Exit(ErrorExitCode);
        }

        private static void Work(Params parameters)
        {
            try
            {
                Console.CancelKeyPress += (sender, a) => Environment.Exit(ErrorExitCode);
                using (var source = new FileStream(parameters.From, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using (var dest = new FileStream(parameters.To, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        using (var compressor = new DummyCompressor(parameters.Mode))
                        {
                            compressor.OnProgress += Compressor_OnProgress                            ;
                            compressor.Process(source, dest);
                        }
                    }
                }
            }
            catch (AggregateException ex)
            {
                throw new Exception("При обработке возникла одна или несколько ошибок:" + Environment.NewLine + string.Join(Environment.NewLine, ex.InnerExceptions.Select(e=>e.Message).ToArray()));
            }
            catch (FileNotFoundException ex)
            {
                throw new Exception($"Файл {ex.FileName} не найден.");
            }
        }

        private static void Compressor_OnProgress(object sender, ProgressEventArgs e)
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write($"Завершено: {e.Progress}%");
        }

        private static Params ReadParameters(string[] args)
        {
            if (args.Length != 3)
                throw new BadParamsException("Неверное количество параметров.");

            var result = new Params
            {
                Mode = args[0] == "compress"
                    ? CompressionMode.Compress
                    : (args[0] == "decompress" ? CompressionMode.Decompress : throw new ArgumentOutOfRangeException("mode", "Некорректный режим работы")),
                From = args[1],
                To = args[2]
            };
            return result;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Использование:");
            Console.WriteLine("GZipTest.exe <mode> <имя исходного файла> <имя результирующего файла>");
            Console.WriteLine("\t <mode>: ");
            Console.WriteLine("\t compress: Упаковка");
            Console.WriteLine("\t decompress: Распаковка");
        }

        private class Params
        {
            public CompressionMode Mode { get; set; }
            public string From { get; set; }
            public string To { get; set; }
        }
    }
}
