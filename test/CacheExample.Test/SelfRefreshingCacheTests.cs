using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Xunit;

namespace CacheExample.Test
{
    public class SelfRefreshingCacheTests
    {
        private readonly ILogger<SelfRefreshingCacheTests> _logger;
        public SelfRefreshingCacheTests()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            var logPath = Path.Combine(Directory.GetCurrentDirectory(),"Log", "mytest-.log");
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.File(logPath, LogEventLevel.Debug)
                .MinimumLevel.Debug()
                .CreateLogger();

            var serviceProvider = services.BuildServiceProvider();
            _logger = serviceProvider.GetService<Microsoft.Extensions.Logging.ILogger<SelfRefreshingCacheTests>>();
        }

        [Fact]
        public void SimpleTest()
        {

            using var cache = new SelfRefreshingCache<long>(_logger, 5, 10, () => DateTime.Now.Ticks);
            var data = cache.GetOrCreate();
            var data2 = cache.GetOrCreate();

            Thread.Sleep(TimeSpan.FromSeconds(40));

            var data3 = cache.GetOrCreate();

            Assert.Equal(data, data2);
            Assert.NotEqual(data, data3);

            Log.Debug("********************************");
        }

        [Fact]
        public void WithErrorTest()
        {

            using var cache = new SelfRefreshingCache<string>(_logger, 5, 150, GetSomeValue);
            var data = cache.GetOrCreate();
            var data2 = cache.GetOrCreate();

            Thread.Sleep(TimeSpan.FromSeconds(40));

            var data3 = cache.GetOrCreate();

            Assert.Equal(data, data2);
            Assert.Equal(data, data3);
            Log.Debug("********************************");
        }

        [Fact]
        public void WithErrorReloadSuccessTest()
        {

            using var cache = new SelfRefreshingCache<string>(_logger, 5, 10, GetSomeValue);
            var data = cache.GetOrCreate();
            var data2 = cache.GetOrCreate();

            Thread.Sleep(TimeSpan.FromSeconds(40));

            var data3 = cache.GetOrCreate();

            Assert.Equal(data, data2);
            Assert.NotEqual(data, data3);
            Log.Debug("********************************");
        }

        private static bool _stopRun;

        private static string GetSomeValue()
        {
            if (_stopRun)
            {
                _stopRun = false;
                throw new Exception("some exception in target function");
            }

            var result = DateTime.Now.Ticks.ToString();
            for (var i = 0; i < 1000; i++)
            {
                result += i;
            }

            _stopRun = true;
            return result;
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(loggerBuilder =>
            {
                loggerBuilder.ClearProviders();
                loggerBuilder.AddSerilog();
            });
        }
    }
}
