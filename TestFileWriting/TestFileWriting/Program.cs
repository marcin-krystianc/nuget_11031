using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace TestFileWriting
{
    class Program
    {
        private static long WrittenBytes;
        private static long WrittenFiles;
        private static string TmpRoot = Path.Combine(Path.GetTempPath(), "FileWriting");

        public class Options
        {
            [Option(Required = false, Default = false, HelpText = "Use memory mapped files")]
            public bool? MemoryMaps { get; set; }

            [Option(Required = false, Default = false, HelpText = "Use file streams")]
            public bool? FileStreams { get; set; }

            [Option(Required = false, Default = "c1B", HelpText = "Minimum file size")]
            public string MinSize { get; set; }

            // https://github.com/NuGet/NuGet.Client/blob/744e9ae2e60501709a48081694891722c33dc9b3/src/NuGet.Core/NuGet.Packaging/PackageExtraction/StreamExtensions.cs#L16
            [Option(Required = false, Default = "10MB", HelpText = "Maximum file size")]
            public string MaxSize { get; set; }

            [Option(Required = false, Default = null, HelpText = "Maximum time to run the test app")]
            public long StopAfter { get; set; }

            internal long MinSizeValue => FromSize(MinSize);
            internal long MaxSizeValue => FromSize(MaxSize);

            internal TimeSpan? StopAfterValue => StopAfter == 0 ? null : TimeSpan.FromSeconds(StopAfter);
        }

        static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console(theme: ConsoleTheme.None)
                .CreateLogger();

            Log.Information($"OSDescription:{System.Runtime.InteropServices.RuntimeInformation.OSDescription}, " +
                            $"OSArchitecture:{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}, " +
#if !NETFRAMEWORK
                            $"RuntimeIdentifier:{System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}, " +
#endif
                            $"ProcessArchitecture:{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, " +
                            $"FrameworkDescription:{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, " +
                            $"Environment.ProcessorCount:{Environment.ProcessorCount}, " +
                            "");

            var ctSource = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) => ctSource.Cancel();

            try
            {
                await Parser.Default.ParseArguments<Options>(args).WithParsedAsync(async options =>
                {
                    var tasks = new List<Task>();
                    tasks.Add(KeyboardInputTask(ctSource));
                    tasks.Add(StatsTask(ctSource.Token));
                    if (options.MemoryMaps == true)
                        tasks.Add(MapTask(ctSource.Token, (int) options.MinSizeValue, (int) options.MaxSizeValue));
                    if (options.FileStreams == true)
                        tasks.Add(StreamTask(ctSource.Token, (int) options.MinSizeValue, (int) options.MaxSizeValue));
                    if (options.StopAfterValue != null)
                        tasks.Add(TimeoutTask(ctSource.Token, options.StopAfterValue.Value));

                    await await Task.WhenAny(tasks);
                });
            }
            finally
            {
                ctSource.Cancel();
                Console.WriteLine("Cleaning up files.");
                var directoryInfo = new DirectoryInfo(TmpRoot);
                foreach (var file in directoryInfo.EnumerateFiles())
                {
                    file.Delete();
                }
            }
        }

        // Parse a file size.
        static long FromSize(string v)
        {
            var suffixes = new[] {"b", "kb", "mb", "gb", "tb"};
            var multipliers = Enumerable.Range(0, suffixes.Length)
                .ToDictionary(i => suffixes[i], i => 1L << (10 * i), StringComparer.OrdinalIgnoreCase);

            var suffix = suffixes
                .Select(suffix => (v.EndsWith(suffix, StringComparison.OrdinalIgnoreCase), suffix))
                .Reverse()
                .Where(x => x.Item1 == true)
                .Select(x => x.suffix)
                .FirstOrDefault();

            if (suffix != null)
            {
                v = v.Substring(0, v.Length - suffix.Length);
            }

            var result = long.Parse(v);

            if (suffix != null)
            {
                result *= multipliers[suffix];
            }

            return result;
        }

        static string ToSize(long bytes)
        {
            const int scale = 1024;
            string[] orders = new string[] {"GB", "MB", "KB", "Bytes"};
            long max = (long) Math.Pow(scale, orders.Length - 1);

            foreach (string order in orders)
            {
                if (bytes >= max)
                    return string.Format("{0:###.#0} {1}", decimal.Divide(bytes, max), order);

                max /= scale;
            }

            return "0 Bytes";
        }

        static async Task KeyboardInputTask(CancellationTokenSource ctSource)
        {
            await Task.Run(() => Console.ReadLine());
            ctSource.Cancel();
        }

        static async Task StatsTask(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));

            var swGlobal = Stopwatch.StartNew();
            var stats = new Queue<(long writtenBytes, long writtenFiles)>();
            var filesRate = "N/A";
            var writeRate = "N/A";
            while (true)
            {
                if (ct.IsCancellationRequested)
                    return;

                var sw = Stopwatch.StartNew();
                var writtenBytes = Interlocked.Read(ref WrittenBytes);
                var writtenFiles = Interlocked.Read(ref WrittenFiles);

                if (stats.Count > 0)
                {
                    var oldInfo = stats.Count == 60
                        ? stats.Dequeue()
                        : stats.Peek();

                    var bytesDiff = writtenBytes - oldInfo.writtenBytes;
                    writeRate = $"{ToSize(bytesDiff / stats.Count)}/s";

                    var filesDiff = writtenFiles - oldInfo.writtenFiles;
                    filesRate = $"{filesDiff * 60 / stats.Count}/min";
                }

                stats.Enqueue((writtenBytes, writtenFiles));

                Log.Information($"Elapsed:{(int) swGlobal.Elapsed.TotalSeconds,3:N0}s, " +
                                $"filesRate:{filesRate}, " +
                                $"writeRate:{writeRate}, " +
                                $"totalBytes:{ToSize(writtenBytes)}, " +
                                "");

                var elapsed = sw.Elapsed;
                if (elapsed < TimeSpan.FromSeconds(1))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1) - elapsed);
                }
            }
        }

        static async Task MapTask(CancellationToken ct, int minSize, int maxSize)
        {
            Log.Information($"Starting MapTask(minSize={ToSize(minSize)}, maxSize={ToSize(maxSize)})");
            await Task.Yield();

            var rnd = new Random();
            var bytes = new byte[maxSize];
            rnd.NextBytes(bytes);

            Directory.CreateDirectory(TmpRoot);

            while (true)
            {
                if (ct.IsCancellationRequested)
                    return;

                var tmpPath = Path.Combine(TmpRoot, Path.GetRandomFileName());
                var size = rnd.Next(minSize, maxSize);
                using (var ms = new MemoryStream(bytes, 0, size))
                using (var mmf = MemoryMappedFile.CreateFromFile(tmpPath, FileMode.OpenOrCreate, null, size))
                {
                    // Create the memory-mapped file.
                    using (MemoryMappedViewStream mmstream = mmf.CreateViewStream())
                    {
                        ms.CopyTo(mmstream);
                    }
                }

                Interlocked.Add(ref WrittenBytes, size);
                Interlocked.Increment(ref WrittenFiles);
            }
        }

        static async Task TimeoutTask(CancellationToken ct, TimeSpan timeout)
        {
            Log.Information($"Starting TimeoutTask(timeout={timeout})");

            await Task.Delay(timeout, ct);
        }

        static async Task StreamTask(CancellationToken ct, int minSize, int maxSize)
        {
            Log.Information($"Starting StreamTask(minSize={ToSize(minSize)}, maxSize={ToSize(maxSize)})");
            await Task.Yield();

            var rnd = new Random();
            var bytes = new byte[maxSize];
            rnd.NextBytes(bytes);
            Directory.CreateDirectory(TmpRoot);

            while (true)
            {
                if (ct.IsCancellationRequested)
                    return;

                var tmpPath = Path.Combine(TmpRoot, Path.GetRandomFileName());
                var size = rnd.Next(minSize, maxSize);
                using (var ms = new MemoryStream(bytes, 0, size))
                using (var outputStream = new FileStream(tmpPath, FileMode.Create))
                {
                    ms.CopyTo(outputStream);
                }

                Interlocked.Add(ref WrittenBytes, size);
                Interlocked.Increment(ref WrittenFiles);
            }
        }
    }
}