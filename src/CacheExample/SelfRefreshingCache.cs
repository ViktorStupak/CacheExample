using System;
using System.Runtime.Caching;
using Microsoft.Extensions.Logging;

namespace CacheExample
{
    /// <summary>
    /// self-refreshing cache container.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <seealso cref="System.IDisposable" />
    public class SelfRefreshingCache<TResult>:IDisposable
    {
        private readonly ILogger _logger;
        private readonly MemoryCache _cache;
        private readonly Func<TResult> _targetFunction;
        private readonly string _cacheKey;
        private readonly object _lock = new object();
        private readonly int _refreshPeriodSeconds;
        private readonly int _validityOfResultSeconds;

        private bool _disposed;

        /// <summary>
        /// Initialize instance.
        /// </summary>
        /// <param name="logger">logger instance to report errors</param>
        /// <param name="refreshPeriodSeconds">interval of automatic background refresh (in seconds)</param>
        /// <param name="validityOfResultSeconds">how long can we keep returning one result. This property has its importance when createFunction fails during refresh – in that case we keep returning the previously created result until its validity expires.</param>
        /// <param name="createFunction">function, that creates the TResult object. CreateFunction can be time consuming, e.g. a download function from Web or Database, or it can be some CPU heavy calculation.</param>
        public SelfRefreshingCache(ILogger logger,
                                   int refreshPeriodSeconds,
                                   int validityOfResultSeconds,
                                   Func<TResult> createFunction)
        {
            this._logger = logger;
            this._targetFunction = createFunction;
            this._cacheKey = _targetFunction.GetHashCode().ToString();
            this._cache = new MemoryCache(this._cacheKey);
            this._refreshPeriodSeconds = refreshPeriodSeconds;
            this._validityOfResultSeconds = validityOfResultSeconds;
            WriteLog(_logger, LogLevel.Debug, $"Init SelfRefreshingCache for key:{this._cacheKey}");
        }

        /// <summary>
        /// Get item from cache or create new if absent.
        /// </summary>
        /// <remarks>
        /// <para>if called for the first time, then also starts automatic refreshes</para>
        /// <para>if valid result exists, returns it immediately</para>
        /// <para>in reasonable cases, e.g. when the result cannot be obtained, throws an exception</para>
        /// </remarks>
        /// <returns></returns>
        public TResult GetOrCreate()
        {
            TResult result;
            lock (_lock)
            {
                result = GetById(this._cacheKey);
            }
            if (result != null && !result.Equals(default(TResult)))
            {
                WriteLog(_logger, LogLevel.Debug, $"Get cache for entity:{_cacheKey}");
                return result;
            }
            result = _targetFunction.Invoke();
            lock (_lock)
            {
                _cache.Set(_cacheKey, result, GetPolicy(_refreshPeriodSeconds, _validityOfResultSeconds));
            }
            WriteLog(_logger, LogLevel.Debug, $"Set new cache for entity:{_cacheKey}");
            return result;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose() => Dispose(true);

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // Dispose managed state (managed objects).
                _cache.Dispose();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            _disposed = true;
        }

        /// <summary>
        /// Get item from cache by identifier.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns><see cref="TResult"/> or <c>default</c> of the <see cref="TResult"/> e.g. null, 0 etc.</returns>
        private TResult GetById(string id)
        {
            var value = this._cache.Get(id);
            if (value == null)
            {
                return default;
            }

            return (TResult)value;
        }

        /// <summary>
        /// Gets the policy for eviction and expiration details for a specific cache entry.
        /// </summary>
        /// <param name="refreshPeriodSeconds">The refresh period seconds.</param>
        /// <param name="validityOfResultSeconds">The validity of result seconds.</param>
        /// <returns></returns>
        private CacheItemPolicy GetPolicy(int refreshPeriodSeconds, int? validityOfResultSeconds)
        {
            return new CacheItemPolicy
            {
                // This is where the magic happens
                // The UpdateCallback will be called before our object is removed from the cache
                UpdateCallback = args =>
                {

                    if (args.RemovedReason != CacheEntryRemovedReason.Expired)
                    {
                        WriteLog(_logger, LogLevel.Debug, $"UpdateCallback called for reason: {args.RemovedReason}");
                        return;
                    }

                    var cacheKey = args.Key;

                    lock (_lock)
                    {
                        // Get current cached value
                        var currentCachedEntity = (TResult)args.Source.Get(this._cacheKey);

                        // Get the potentially new data
                        TResult newEntity;
                        try
                        {
                            newEntity =  _targetFunction.Invoke();
                        }
                        catch (Exception e)
                        {
                            WriteLog(_logger, LogLevel.Error, $"target function return error: {e.Message}");
                            newEntity = default;
                        }

                        // If new is not null - update, otherwise just refresh the old value
                        // The condition by which you decide to update or refresh the data depends entirely on you
                        if (newEntity != null && !newEntity.Equals(default(TResult)))
                        {
                            var updatedEntity = newEntity;
                            args.UpdatedCacheItem = new CacheItem(cacheKey, updatedEntity);
                            args.UpdatedCacheItemPolicy = GetPolicy(_refreshPeriodSeconds, _validityOfResultSeconds);
                            WriteLog(_logger, LogLevel.Debug, $"cache for entity:{cacheKey} is update");
                        }
                        else
                        {
                            if (validityOfResultSeconds != null)
                            {
                                var updatedEntity = currentCachedEntity;
                                args.UpdatedCacheItem = new CacheItem(cacheKey, updatedEntity);
                                args.UpdatedCacheItemPolicy = GetPolicy((int)validityOfResultSeconds, null);
                                WriteLog(_logger, LogLevel.Debug, $"cache for entity:{cacheKey} returned to previously state");
                            }
                            else
                            {
                                args.Source.Remove(cacheKey);
                                WriteLog(_logger, LogLevel.Critical, $"Cannot update cache. entity:{cacheKey} was cleaned");
                            }
                        }
                    }
                },
                AbsoluteExpiration = DateTime.UtcNow.AddSeconds(refreshPeriodSeconds),
            };
        }

        private static void WriteLog(ILogger logger, LogLevel level, string message)
        {
            logger?.Log(level, message);
        }
    }

}
