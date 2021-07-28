using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace TestFileWriting
{
    class Program
    {
        private const int MAX_MMAP_SIZE = 10 * 1024 * 1024;
        private static long WrittenBytes = 0;
        private static long WrittenFiles = 0;
        
        public class Options
        {
            [Option(Required = false, Default = false, HelpText = "Use memory mapped files")]
            public bool? MemoryMaps { get; set; }    
            
            [Option(Required = false, Default = false, HelpText = "Use file streams")]
            public bool? FileStreams { get; set; }
        }

        static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console(theme: ConsoleTheme.None)
                .CreateLogger();
            
            Log.Information($"OSDescription:{System.Runtime.InteropServices.RuntimeInformation.OSDescription}, " +
                            $"OSArchitecture:{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}, " +
                            $"RuntimeIdentifier:{System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}, " +
                            $"ProcessArchitecture:{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, " +
                            $"FrameworkDescription:{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, " +
                            $"Environment.ProcessorCount:{Environment.ProcessorCount}, " +
                            "");

            await Parser.Default.ParseArguments<Options>(args).WithParsedAsync(async options =>
            {
                var tasks = new List<Task>();
                tasks.Add(StatsTask());
                if (options.MemoryMaps == true) tasks.Add(MapTask());
                if (options.FileStreams == true) tasks.Add(StreamTask());

                await await Task.WhenAny(tasks);
            });
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

        static async Task StatsTask()
        {
            var swGlobal = Stopwatch.StartNew();
            var stats = new Queue<(long writtenBytes, long writtenFiles)>();
            var filesRate = "N/A";
            var writeRate = "N/A";
            while (true)
            {
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

        static async Task MapTask()
        {
            Log.Information($"Starting MapTask()");
            await Task.Delay(TimeSpan.FromSeconds(1));

            var rnd = new Random();
            var bytes = new byte[MAX_MMAP_SIZE];
            rnd.NextBytes(bytes); Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "FileWriting"));

            while (true)
            {  
                var tmpPath = Path.Combine(Path.GetTempPath(), "FileWriting", Path.GetRandomFileName());

                var size = rnd.Next(MAX_MMAP_SIZE);
                using (var ms = new MemoryStream(bytes, 0, size))
                using (var mmf = MemoryMappedFile.CreateFromFile(tmpPath, FileMode.OpenOrCreate, null, size))
                {
                    // Create the memory-mapped file.
                    await using MemoryMappedViewStream mmstream = mmf.CreateViewStream();
                    ms.CopyTo(mmstream);
                }

                Interlocked.Add(ref WrittenBytes, size);
                Interlocked.Increment(ref WrittenFiles);
            }
        }

        static async Task StreamTask()
        {
            Log.Information($"Starting StreamTask()");
            await Task.Delay(TimeSpan.FromSeconds(1));

            var rnd = new Random();
            var bytes = new byte[MAX_MMAP_SIZE];
            rnd.NextBytes(bytes);
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "FileWriting"));
            
            while (true)
            {
                var tmpPath = Path.Combine(Path.GetTempPath(), "FileWriting", Path.GetRandomFileName());

                var size = rnd.Next(MAX_MMAP_SIZE);
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