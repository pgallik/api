namespace Be.Vlaanderen.Basisregisters.Api
{
    using System;
    using System.Data.SqlClient;
    using System.IO;
    using System.Text;
    using Autofac;
    using DataDog.Tracing;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Polly;
    using Serilog;
    using SqlStreamStore;

    public static class StartupHelpers
    {
        public const string AllowSpecificOrigin = "AllowSpecificOrigin";

        public static void RegisterApplicationLifetimeHandling(
            IContainer applicationContainer,
            IApplicationLifetime appLifetime,
            TraceAgent traceAgent)
        {
            appLifetime.ApplicationStarted.Register(() => Log.Information("Application started."));

            appLifetime.ApplicationStopping.Register(() =>
            {
                traceAgent?.OnCompleted();
                traceAgent?.Completion.Wait();

                Log.Information("Application stopping.");
                Log.CloseAndFlush();
            });

            appLifetime.ApplicationStopped.Register(applicationContainer.Dispose);

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                appLifetime.StopApplication();

                // Don't terminate the process immediately, wait for the Main thread to exit gracefully.
                eventArgs.Cancel = true;
            };
        }

        public static void EnsureSqlStreamStoreSchema<T>(MsSqlStreamStore streamStore, ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger<T>();

            // TODO: Need to revisit this with a Consul lock
            Policy
                .Handle<SqlException>()
                .WaitAndRetry(
                    5,
                    retryAttempt =>
                    {
                        var value = Math.Pow(2, retryAttempt) / 4;
                        var randomValue = new Random().Next((int)value * 3, (int)value * 5);
                        logger.LogInformation("Retrying after {Seconds} seconds...", randomValue);
                        return TimeSpan.FromSeconds(randomValue);
                    })
                .Execute(() =>
                {
                    logger.LogInformation("Ensuring the sql stream store schema.");

                    var checkSchemaResult = streamStore.CheckSchema().GetAwaiter().GetResult();
                    if (!checkSchemaResult.IsMatch())
                        streamStore.CreateSchema().GetAwaiter().GetResult();
                });
        }

        public static void SetupSourceListener(TraceSource source)
        {
            var serializer = new JsonSerializer { Formatting = Formatting.Indented };

            source.Subscribe(t =>
            {
                var sb = new StringBuilder("========== Begin Trace ==========");

                using (var writer = new StringWriter(sb))
                {
                    writer.WriteLine();
                    serializer.Serialize(writer, t);
                    writer.WriteLine("========== End Trace ==========");
                    writer.Flush();
                }

                Console.WriteLine(sb.ToString());
            });
        }
    }
}