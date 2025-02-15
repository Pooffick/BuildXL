// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Creates <see cref="IContentLocationStore"/> instance backed by Local Location Store.
    /// </summary>
    public class ContentLocationStoreFactory : StartupShutdownBase, IContentLocationStoreFactory, IRedisDatabaseFactory
    {
        /// <summary>
        /// Default value for keyspace used for partitioning Redis data
        /// </summary>
        public const string DefaultKeySpace = "Default:";

        /// <inheritdoc />
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new ContentSessionTracer(nameof(ContentLocationStoreFactory));

        // https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Basics.md
        // Maintain the same connection multiplexer to reuse across sessions

        /// <nodoc />
        protected IClock Clock => Arguments.Clock;

        /// <nodoc />
        protected DistributedContentCopier Copier => Arguments.Copier;

        /// <nodoc />
        protected string KeySpace => Configuration.Keyspace;

        /// <nodoc />
        protected readonly RedisContentLocationStoreConfiguration Configuration;

        protected ContentLocationStoreFactoryArguments Arguments { get; }

        public IRoleObserver? Observer { get; set; }

        internal  IGlobalCacheService? LocalGlobalCacheService { get; set; }

        internal IClientAccessor<MachineLocation, IGlobalCacheService>? GlobalCacheServiceClientFactory { get; set; }

        /// <nodoc />
        public RedisDatabaseFactory? RedisDatabaseFactoryForRedisGlobalStore;

        /// <nodoc />
        public RedisDatabaseFactory? RedisDatabaseFactoryForRedisGlobalStoreSecondary;

        private ColdStorage? _coldStorage;

        private readonly Lazy<LocalLocationStore> _lazyLocalLocationStore;

        public ContentLocationStoreFactory(
            IClock clock,
            RedisContentLocationStoreConfiguration configuration,
            DistributedContentCopier copier)
            : this(
                new ContentLocationStoreFactoryArguments()
                {
                    Clock = clock,
                    Copier = copier
                },
                configuration)
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentLocationStoreFactory"/> class.
        /// </summary>
        public ContentLocationStoreFactory(
            ContentLocationStoreFactoryArguments arguments,
            RedisContentLocationStoreConfiguration configuration)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(!string.IsNullOrEmpty(configuration.RedisGlobalStoreConnectionString));
            Contract.Requires(!string.IsNullOrWhiteSpace(configuration.Keyspace));
            Contract.Requires(arguments.Copier != null);

            Arguments = arguments;
            _lazyLocalLocationStore = new Lazy<LocalLocationStore>(() => CreateLocalLocationStore());
            Configuration = configuration;
        }

        /// <inheritdoc />
        public Task<IContentLocationStore> CreateAsync(MachineLocation localMachineLocation, ILocalContentStore? localContentStore)
        {
            IContentLocationStore contentLocationStore = new TransitioningContentLocationStore(
                Configuration,
                _lazyLocalLocationStore.Value,
                localMachineLocation,
                localContentStore);

            return Task.FromResult(contentLocationStore);
        }

        /// <summary>
        /// Creates an instance of <see cref="LocalLocationStore"/>.
        /// </summary>
        private LocalLocationStore CreateLocalLocationStore()
        {
            Contract.Assert(RedisDatabaseFactoryForRedisGlobalStore != null);

            var redisStore = CreateRedisGlobalStore();
            var masterElectionMechanism = CreateMasterElectionMechanism(redisStore);
            var globalStore = CreateGlobalCacheStore(redisStore, masterElectionMechanism);
            var localLocationStore = new LocalLocationStore(Clock, redisStore, globalStore, Configuration, Copier, masterElectionMechanism, _coldStorage);
            return localLocationStore;
        }

        private IGlobalCacheStore CreateGlobalCacheStore(RedisGlobalStore redisStore, IMasterElectionMechanism masterElectionMechanism)
        {
            if (Configuration.AllContentMetadataStoreModeFlags.HasAnyFlag(ContentMetadataStoreModeFlags.Distributed))
            {
                var distributedStore = CreateDistributedContentMetadataStore(redisStore, masterElectionMechanism);

                if (!Configuration.AllContentMetadataStoreModeFlags.HasAnyFlag(ContentMetadataStoreModeFlags.Redis))
                {
                    return distributedStore;
                }

                return new TransitioningGlobalCacheStore(Configuration, redisStore, distributedStore);
            }
            else
            {
                return redisStore;
            }
        }

        private IMasterElectionMechanism CreateMasterElectionMechanism(RedisGlobalStore redisStore)
        {
            IMasterElectionMechanism createInner()
            {
                if (Configuration.AzureBlobStorageMasterElectionMechanismConfiguration is not null)
                {
                    var storageElectionMechanism = new AzureBlobStorageMasterElectionMechanism(Configuration.AzureBlobStorageMasterElectionMechanismConfiguration, Configuration.PrimaryMachineLocation, Clock);

                    return storageElectionMechanism;
                }
                else
                {
                    return redisStore;
                }
            }

            var inner = createInner();
            if (Observer != null)
            {
                return new ObservableMasterElectionMechanism(inner, Observer);
            }
            else
            {
                return inner;
            }
        }

        private IGlobalCacheStore CreateDistributedContentMetadataStore(RedisGlobalStore redisStore, IMasterElectionMechanism masterElectionMechanism)
        {
            if (Configuration.MetadataStore is MemoryContentMetadataStoreConfiguration memoryConfig)
            {
                return memoryConfig.Store;
            }
            else if (Configuration.MetadataStore is ClientContentMetadataStoreConfiguration clientConfig)
            {
                var masterClientFactory = new GrpcMasterClientFactory<IGlobalCacheService>(redisStore, GlobalCacheServiceClientFactory!, masterElectionMechanism);

                return new ClientGlobalCacheStore(
                    redisStore,
                    masterClientFactory,
                    clientConfig);
            }
            else
            {
                return redisStore;
            }
        }

        /// <summary>
        /// Creates an instance of <see cref="IGlobalLocationStore"/>.
        /// </summary>
        protected virtual RedisGlobalStore CreateRedisGlobalStore()
        {
            var redisDatabaseForGlobalStore = CreateDatabase(RedisDatabaseFactoryForRedisGlobalStore, "primaryRedisDatabase");
            var secondaryRedisDatabaseForGlobalStore = CreateDatabase(
                RedisDatabaseFactoryForRedisGlobalStoreSecondary,
                "secondaryRedisDatabase",
                optional: true);

            RedisDatabaseAdapter? redisBlobDatabase;
            RedisDatabaseAdapter? secondaryRedisBlobDatabase;
            if (Configuration.UseSeparateConnectionForRedisBlobs)
            {
                // To prevent blob opoerations from blocking other operations, create a separate connections for them.
                redisBlobDatabase = CreateDatabase(RedisDatabaseFactoryForRedisGlobalStore, "primaryRedisBlobDatabase");
                secondaryRedisBlobDatabase = CreateDatabase(
                    RedisDatabaseFactoryForRedisGlobalStoreSecondary,
                    "secondaryRedisBlobDatabase",
                    optional: true);
            }
            else
            {
                redisBlobDatabase = redisDatabaseForGlobalStore;
                secondaryRedisBlobDatabase = secondaryRedisDatabaseForGlobalStore;
            }

            var globalStore = new RedisGlobalStore(Clock, Configuration, redisDatabaseForGlobalStore, secondaryRedisDatabaseForGlobalStore, redisBlobDatabase, secondaryRedisBlobDatabase);
            return globalStore;
        }

        internal RedisDatabaseAdapter CreateRedisDatabase(string databaseName)
        {
            return CreateDatabase(RedisDatabaseFactoryForRedisGlobalStore, databaseName)!;
        }

        private RedisDatabaseAdapter? CreateDatabase(RedisDatabaseFactory? factory, string databaseName, bool optional = false)
        {
            if (factory != null)
            {
                var adapterConfiguration = new RedisDatabaseAdapterConfiguration(
                    KeySpace,
                    Configuration.RedisConnectionErrorLimit,
                    Configuration.RedisReconnectionLimitBeforeServiceRestart,
                    databaseName: databaseName,
                    minReconnectInterval: Configuration.MinRedisReconnectInterval,
                    cancelBatchWhenMultiplexerIsClosed: Configuration.CancelBatchWhenMultiplexerIsClosed,
                    treatObjectDisposedExceptionAsTransient: Configuration.TreatObjectDisposedExceptionAsTransient,
                    operationTimeout: Configuration.OperationTimeout,
                    exponentialBackoffConfiguration: Configuration.ExponentialBackoffConfiguration,
                    retryCount: Configuration.RetryCount);

                return new RedisDatabaseAdapter(factory, adapterConfiguration);
            }
            else
            {
                Contract.Assert(optional);
                return null;
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            Tracer.TraceStartupConfiguration(context, Configuration);

            RedisDatabaseFactoryForRedisGlobalStore = await RedisDatabaseFactory.CreateAsync(
                context,
                new LiteralConnectionStringProvider(Configuration.RedisGlobalStoreConnectionString),
                Configuration.RedisConnectionMultiplexerConfiguration);

            if (Configuration.RedisGlobalStoreSecondaryConnectionString != null)
            {
                RedisDatabaseFactoryForRedisGlobalStoreSecondary = await RedisDatabaseFactory.CreateAsync(
                    context,
                    new LiteralConnectionStringProvider(Configuration.RedisGlobalStoreSecondaryConnectionString),
                    Configuration.RedisConnectionMultiplexerConfiguration);
            }

            return BoolResult.Success;
        }

        async Task<RedisDatabaseAdapter> IRedisDatabaseFactory.CreateAsync(OperationContext context, string databaseName, string connectionString)
        {
            var factory = await RedisDatabaseFactory.CreateAsync(
                context,
                new LiteralConnectionStringProvider(connectionString),
                Configuration.RedisConnectionMultiplexerConfiguration);

            return CreateDatabase(factory, databaseName)!;
        }

        public void SetColdStorage(ColdStorage coldStorage)
        {
            // We use the ColdStorage to lazily build the LocalLocationStore later
            _coldStorage = coldStorage;
        }
    }
}
