using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheOrder.Application;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Online;
using TheOrder.Infrastructure.Recognition;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Startup;

internal static class Composition
{
    public static NativeHost Create(LanguageSettings languages, LanguagePreferencesStore languageStore,
        ContentLanguageSettings contentLanguages, ContentLanguagePreferencesStore contentStore)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables("THE_ORDER_NATIVE_").Build();
        var services = new ServiceCollection();
        services.AddSingleton(languages);
        services.AddSingleton<ILanguagePreferencesStore>(_ => languageStore);
        services.AddSingleton(contentLanguages);
        services.AddSingleton<IContentLanguagePreferencesStore>(_ => contentStore);
        services.AddLogging(builder =>
        {
            builder.AddDebug().SetMinimumLevel(LogLevel.Information);
            if (Program.Profile is { } profile) builder.AddProvider(new NativeLifecycleLog(profile));
        });
        var nativeConfiguration = Program.Profile?.Configuration(configuration["DIAGNOSTICS"] == "1")
            ?? new NativeConfiguration(configuration["DATA_DIR"], configuration["DIAGNOSTICS"] == "1");
        services.AddSingleton(nativeConfiguration);
        services.AddSingleton<GitHubUpdateFeed>();
        var fixtureUpdates = Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test
            && configuration["VISUAL_TEST"] == "1" && configuration["UPDATE_FIXTURE"] == "1";
        services.AddSingleton<IAppUpdateFeed>(p => fixtureUpdates ? new SyntheticUpdates() : p.GetRequiredService<GitHubUpdateFeed>());
        services.AddSingleton<WindowsUpdatePackages>();
        services.AddSingleton<IAppUpdatePackages>(p => p.GetRequiredService<WindowsUpdatePackages>());
        services.AddSingleton<IAppUpdateInstaller, WindowsUpdateInstaller>();
        services.AddSingleton(p => new AppUpdateViewModel(p.GetRequiredService<IAppUpdateFeed>(), p.GetRequiredService<IAppUpdatePackages>(),
            p.GetRequiredService<IAppUpdateInstaller>(), AppReleaseIdentity.Version, new UpdateChannelPreferencesStore(nativeConfiguration)));
        services.AddSingleton<IMaterialPreferencesStore, MaterialPreferencesStore>();
        services.AddSingleton<ILibraryCodec, NativeLibraryCodec>();
        services.AddSingleton<ITransactionalLibraryFactory, IsolatedLibraryFactory>();
        services.AddSingleton<OfflineLibraryClient>();
        services.AddSingleton<ILibraryBackupStore, WindowsLibraryBackupStore>();
        services.AddSingleton<LibraryBackupService>();
        services.AddSingleton<LibraryBackupViewModel>();
        services.AddSingleton<IFilesystemIdentity, WindowsFilesystemIdentity>();
        services.AddSingleton<LaunchPolicy>();
        services.AddSingleton<IGlobalLaunchDefaultsStore, GlobalLaunchDefaultsStore>();
        services.AddSingleton<GlobalLaunchSettings>();
        services.AddSingleton<ILaunchLibrary, OfflineLaunchLibrary>();
        services.AddSingleton<ILaunchPositionStore, IsolatedLaunchPositionStore>();
        services.AddSingleton<IGameProcessPlatform, WindowsGameProcesses>();
        services.AddSingleton<WindowsSessionPowerEvents>();
        services.AddSingleton(p => new AppSessionHost(async () =>
        {
            var client = p.GetRequiredService<OfflineLibraryClient>();
            await client.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            if (!client.Capability.Loaded) throw new IOException(client.Capability.Message);
            if (!client.Capability.CanEdit) return null;
            return client.CreateSessionEngine(p.GetRequiredService<IFilesystemIdentity>(), p.GetRequiredService<IGameProcessPlatform>(),
                p.GetRequiredService<ILaunchPositionStore>(), new WindowsSessionClock(), new WindowsSessionRecoveryTokens(nativeConfiguration), p.GetRequiredService<WindowsSessionPowerEvents>(), p.GetRequiredService<LaunchPolicy>());
        }, () => p.GetRequiredService<OfflineLibraryClient>().CurrentSnapshot));
        services.AddSingleton(p => new LaunchViewModel(p.GetRequiredService<ILaunchLibrary>(), p.GetRequiredService<AppSessionHost>()));
        services.AddSingleton<IScanFilesystem, WindowsScanFilesystem>();
        services.AddSingleton<BoundedScanner>();
        services.AddSingleton<ILibrarySourcesStore, WindowsLibrarySourcesStore>();
        services.AddSingleton<ISteamLibraryScanner>(p => new WindowsSteamLibraryScanner(p.GetRequiredService<IFilesystemIdentity>(),
            p.GetRequiredService<BoundedScanner>(), p.GetRequiredService<ITitleIndex>()));
        services.AddSingleton<LibrarySourcesService>();
        services.AddSingleton<WindowsLibrarySourcesWatcher>();
        services.AddSingleton<WindowsLocalResources>();
        services.AddSingleton<LibraryExtrasClient>();
        services.AddSingleton<IDirectMetadataSource>(p => new DirectMetadataSources(p.GetRequiredService<PublicArtworkNetwork>().Client, p.GetRequiredService<BangumiAccessContext>()));
        services.AddSingleton<MetadataSourceClient>();
        services.AddSingleton<LaunchFileDiscovery>();
        services.AddSingleton<IRasterCodec, WindowsRasterCodec>();
        services.AddSingleton<OfflineFilesClient>();
        services.AddSingleton<LocalCoverSources>();
        services.AddSingleton<IsolatedCredentialStore>();
        var fixtureOnline = nativeConfiguration.Diagnostics && configuration["ONLINE_FIXTURE"] == "1";
        if (fixtureOnline)
        {
            services.AddSingleton(_ => new SyntheticOnline(configuration["ONLINE_SLOW_CONNECTION"] == "1", configuration["RECOGNITION_FIXTURE"] == "1", configuration["RECOGNITION_BOUNDARY_FIXTURE"] == "1"));
        }
        var fixtureCredentials = fixtureOnline && configuration["ONLINE_REAL_STORE"] != "1";
        var fixturePersonal = fixtureOnline && Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test
            && configuration["VISUAL_TEST"] == "1" && configuration["PERSONAL_FIXTURE"] == "1";
        if (fixturePersonal) services.AddSingleton<SyntheticNextMoe>();
        services.AddSingleton<INextMoeAuthStore, WindowsNextMoeAuthStore>();
        services.AddSingleton<NextMoeAccountNetwork>();
        services.AddSingleton<INextMoeOAuth>(p => fixturePersonal ? p.GetRequiredService<SyntheticNextMoe>() : new NextMoeOAuth(p.GetRequiredService<NextMoeAccountNetwork>().Client));
        services.AddSingleton<IOAuthSystemBrowser, WindowsOAuthSystemBrowser>();
        services.AddSingleton<IOAuthLogin>(p => fixturePersonal ? p.GetRequiredService<SyntheticNextMoe>() : new WindowsOAuthLogin(p.GetRequiredService<IOAuthSystemBrowser>()));
        services.AddSingleton(p => new NextMoeAuthentication(
            fixtureCredentials ? p.GetRequiredService<SyntheticOnline>() : p.GetRequiredService<IsolatedCredentialStore>(),
            fixtureCredentials ? p.GetRequiredService<SyntheticOnline>() : p.GetRequiredService<IsolatedCredentialStore>(),
            p.GetRequiredService<INextMoeAuthStore>(), p.GetRequiredService<INextMoeOAuth>(), p.GetRequiredService<IOAuthLogin>()));
        services.AddSingleton<ICredentialSettings>(p => p.GetRequiredService<NextMoeAuthentication>());
        services.AddSingleton<ICredentialProvider>(p => p.GetRequiredService<NextMoeAuthentication>());
        services.AddSingleton<INextMoeAccess>(p => p.GetRequiredService<NextMoeAuthentication>());
        services.AddSingleton<NextMoeRateCoordinator>();
        services.AddSingleton<CatalogNetwork>();
        services.AddSingleton<ImageNetwork>();
        services.AddSingleton<PublicArtworkNetwork>();
        services.AddSingleton<BangumiAccessContext>();
        services.AddSingleton<IBangumiCredentialStore, WindowsBangumiCredentialStore>();
        services.AddSingleton<IBangumiAccountProvider>(p => new BangumiAccountProvider(p.GetRequiredService<PublicArtworkNetwork>().Client));
        services.AddSingleton<IBangumiOAuthBrowser, WindowsBangumiOAuthBrowser>();
        services.AddSingleton<IBangumiOAuth>(p => new BangumiOAuthProvider(p.GetRequiredService<PublicArtworkNetwork>().Client,
            p.GetRequiredService<IBangumiOAuthBrowser>(), BangumiOAuthDefaults.Service));
        services.AddSingleton<BangumiAuthorization>();
        services.AddSingleton<BangumiSettingsViewModel>();
        services.AddSingleton(p => new BangumiRelationsProvider(p.GetRequiredService<PublicArtworkNetwork>().Client, access: p.GetRequiredService<BangumiAccessContext>()));
        services.AddSingleton<IBangumiRelationsProvider>(p => p.GetRequiredService<BangumiRelationsProvider>());
        services.AddSingleton<IRelatedSemanticReader>(p => p.GetRequiredService<BangumiRelationsProvider>());
        services.AddSingleton(p => new BangumiSubjectDetailsProvider(p.GetRequiredService<PublicArtworkNetwork>().Client, access: p.GetRequiredService<BangumiAccessContext>()));
        services.AddSingleton<IBangumiSubjectDetailsProvider>(p => p.GetRequiredService<BangumiSubjectDetailsProvider>());
        services.AddSingleton<IBangumiSubjectDetailsCache, IsolatedBangumiSubjectDetailsCache>();
        services.AddSingleton<IBangumiSubjectDetailsReader>(p => new BangumiSubjectDetailsService(p.GetRequiredService<IBangumiSubjectDetailsProvider>(), access: p.GetRequiredService<BangumiAccessContext>(), cache: p.GetRequiredService<IBangumiSubjectDetailsCache>()));
        services.AddSingleton<IBangumiEpisodesProvider>(p => p.GetRequiredService<BangumiSubjectDetailsProvider>());
        services.AddSingleton<IBangumiEpisodesReader>(p => new BangumiEpisodesService(p.GetRequiredService<IBangumiEpisodesProvider>(), access: p.GetRequiredService<BangumiAccessContext>()));
        services.AddSingleton<IMusicBrainzProvider>(p => new MusicBrainzProvider(p.GetRequiredService<PublicArtworkNetwork>().Client));
        services.AddSingleton<IAppleCatalogProvider>(p => new AppleCatalogProvider(p.GetRequiredService<PublicArtworkNetwork>().Client));
        services.AddSingleton<IMusicBrainzEntityReader>(p => (IMusicBrainzEntityReader)p.GetRequiredService<IMusicBrainzProvider>());
        services.AddSingleton<IMusicBrainzCache, IsolatedMusicBrainzCache>();
        services.AddSingleton<IMediaIdentityDecisions, IsolatedMediaIdentityDecisions>();
        services.AddSingleton<IGameVideoProvider>(p => new IgdbGameVideoProvider(p.GetRequiredService<PublicArtworkNetwork>().Client,
            Uri.TryCreate(configuration["IGDB_SERVICE_URL"], UriKind.Absolute, out var service) ? service : null,
            Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test));
        services.AddSingleton<IGameVideoCache, IsolatedGameVideoCache>();
        services.AddSingleton<GameVideoReader>();
        services.AddSingleton<IMusicBrainzReader, MusicBrainzService>();
        services.AddSingleton<IAniListProvider>(p => new AniListProvider(p.GetRequiredService<PublicArtworkNetwork>().Client));
        services.AddSingleton<IAniListCache, IsolatedAniListCache>();
        services.AddSingleton<IBookOutletSupplementProvider>(p => new MangaDexBookLinksProvider(p.GetRequiredService<PublicArtworkNetwork>().Client));
        services.AddSingleton<IAniListReader, AniListService>();
        services.AddSingleton<IsolatedBookBibliographyStore>();
        services.AddSingleton<IBookBibliographyCache>(p => p.GetRequiredService<IsolatedBookBibliographyStore>());
        services.AddSingleton<IBookPublicationBindings>(p => p.GetRequiredService<IsolatedBookBibliographyStore>());
        services.AddSingleton<IBookBibliographyProvider>(p => new NdlBibliographyProvider(p.GetRequiredService<PublicArtworkNetwork>().Client));
        services.AddSingleton<IBookBibliographyReader, BookBibliographyService>();
        services.AddSingleton<IBangumiBookRelationsProvider>(p => p.GetRequiredService<BangumiRelationsProvider>());
        services.AddSingleton<IBangumiBookRelationsReader>(p => new BangumiBookRelationsService(p.GetRequiredService<IBangumiBookRelationsProvider>(), p.GetRequiredService<BangumiAccessContext>()));
        services.AddSingleton<IWatchLinkPreferencesStore, WatchLinkPreferencesStore>();
        services.AddSingleton<IBangumiWatchDataProvider>(p => new BangumiWatchDataProvider(p.GetRequiredService<PublicArtworkNetwork>().Client));
        services.AddSingleton<IBangumiWatchSnapshotStore, IsolatedWatchDataSnapshotStore>();
        services.AddSingleton<IBangumiWatchLinksReader, BangumiWatchDataService>();
        services.AddSingleton<IBangumiRelationsCache, IsolatedBangumiRelationsCache>();
        services.AddSingleton(p => new BangumiRelationsService(p.GetRequiredService<IBangumiRelationsProvider>(), p.GetRequiredService<IBangumiRelationsCache>(), access: p.GetRequiredService<BangumiAccessContext>()));
        services.AddSingleton<IBangumiRelationsReader>(p => p.GetRequiredService<BangumiRelationsService>());
        // Explicit isolated Test images must not depend on the diagnostic text UI.
        // Production/Development profiles never select this synthetic transport.
        var performanceImages = Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test && configuration["PERFORMANCE_COVERS"] is not null;
        if (performanceImages) services.AddSingleton<PerformanceImageFixture>();
        services.AddSingleton(p => new CatalogTransport(fixtureOnline ? p.GetRequiredService<SyntheticOnline>().Client : p.GetRequiredService<CatalogNetwork>().Client, p.GetRequiredService<ICredentialProvider>(), new Uri("https://api.nextmoe.dev/v2/"), rate: fixtureOnline ? null : p.GetRequiredService<NextMoeRateCoordinator>()));
        services.AddSingleton(p => new NextMoeCatalog(p.GetRequiredService<CatalogTransport>(), clock: fixtureOnline ? () => p.GetRequiredService<SyntheticOnline>().Now : null));
        services.AddSingleton<IOnlineCatalog>(p => p.GetRequiredService<NextMoeCatalog>());
        services.AddSingleton<ICatalogReleaseReader>(p => p.GetRequiredService<NextMoeCatalog>());
        services.AddSingleton<IEntityCatalog>(p => p.GetRequiredService<NextMoeCatalog>());
        services.AddSingleton<IContentFilteredCatalog>(p => p.GetRequiredService<NextMoeCatalog>());
        services.AddSingleton<CatalogBrowsePolicy>();
        services.AddSingleton<CatalogBrowseClient>();
        services.AddSingleton<ICatalogBrowsePreferencesStore, CatalogBrowsePreferencesStore>();
        services.AddSingleton<CatalogBrowseSettings>();
        services.AddSingleton<OnlineCoverSelectionClient>();
        services.AddSingleton<IRecognitionCatalog>(p => p.GetRequiredService<NextMoeCatalog>());
        services.AddSingleton(p => p.GetRequiredService<NextMoeAuthentication>().AuthContext);
        services.AddSingleton<IPersonalSyncStore, WindowsPersonalSyncStore>();
        services.AddSingleton<IPersonalSyncLibrary, PersonalSyncLibrary>();
        var readOnlyAcceptance = Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test && configuration["READ_ONLY_OAUTH_ACCEPTANCE"] == "1";
        services.AddSingleton<INextMoePersonalApi>(p => {
            INextMoePersonalApi api = fixturePersonal ? p.GetRequiredService<SyntheticNextMoe>() : new NextMoePersonalApi(p.GetRequiredService<CatalogNetwork>().Client,
                p.GetRequiredService<INextMoeAccess>(), p.GetRequiredService<NextMoeRateCoordinator>());
            return readOnlyAcceptance ? new ReadOnlyPersonalApi(api) : api;
        });
        services.AddSingleton<PersonalSyncCoordinator>();
        services.AddSingleton<NextMoeSettingsViewModel>();
        services.AddSingleton<ILocalEvidenceParser, LocalEvidenceParser>();
        services.AddSingleton<IExecutableEvidenceParser, ExecutableEvidenceParser>();
        services.AddSingleton<IRecognitionEvidenceReader>(p => new WindowsRecognitionEvidence(p.GetRequiredService<ILocalEvidenceParser>(), RecognitionFingerprint.Evidence, p.GetRequiredService<IExecutableEvidenceParser>()));
        services.AddSingleton<ITitleIndex>(_ => new TitleIndex(() => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "data", "title-index.json.gz"))));
        services.AddSingleton<IRecognitionRanker, ReleaseAwareRanking>();
        services.AddSingleton<IRecognitionEngine, RecognitionEngine>();
        services.AddSingleton<IRecognitionLibrarySource>(p => p.GetRequiredService<OfflineLibraryClient>());
        services.AddSingleton<RecognitionQueue>();
        services.AddSingleton<IRecognitionReviewStorage, IsolatedRecognitionReviewStorage>();
        services.AddSingleton<IRecognitionReviewCodec, RecognitionReviewCodec>();
        services.AddSingleton(p => new RecognitionReviewService(p.GetRequiredService<OfflineLibraryClient>(), p.GetRequiredService<RecognitionQueue>(),
            p.GetRequiredService<IRecognitionReviewStorage>(), p.GetRequiredService<IRecognitionReviewCodec>(), p.GetRequiredService<IRecognitionEvidenceReader>(),
            p.GetRequiredService<IRecognitionRanker>(), p.GetRequiredService<IRecognitionCatalog>(), p.GetRequiredService<OnlineAuthContext>(), RecognitionFingerprint.Identity));
        services.AddSingleton<RecognitionViewModel>();
        services.AddSingleton<IRemoteArtworkCache,IsolatedArtworkCache>();
        services.AddSingleton(p => new RemoteCoverSource(performanceImages ? p.GetRequiredService<PerformanceImageFixture>().Client : fixtureOnline ? p.GetRequiredService<SyntheticOnline>().Client : p.GetRequiredService<ImageNetwork>().Client,p.GetRequiredService<IRemoteArtworkCache>(),
            fixtureOnline || performanceImages ? null : p.GetRequiredService<PublicArtworkNetwork>().Client));
        services.AddSingleton<IRemoteCoverRegistry>(p => p.GetRequiredService<RemoteCoverSource>());
        services.AddSingleton<IPersonalBannerStore, IsolatedPersonalBannerStore>();
        services.AddSingleton(p => new CombinedCoverSources(p.GetRequiredService<LocalCoverSources>(), p.GetRequiredService<OfflineLibraryClient>(), p.GetRequiredService<IRemoteCoverRegistry>(), p.GetRequiredService<RemoteCoverSource>(), p.GetRequiredService<IPersonalBannerStore>()));
        services.AddSingleton<OnlineLibraryClient>();
        services.AddSingleton(p => new OnlineViewModel(p.GetRequiredService<CatalogBrowseClient>(),
            p.GetRequiredService<ICredentialSettings>(), p.GetRequiredService<IRemoteCoverRegistry>(),
            p.GetRequiredService<OnlineLibraryClient>(), p.GetRequiredService<WorkspaceViewModel>()));
        services.AddSingleton<IThumbnailDiskCache, IsolatedThumbnailCache>();
        services.AddSingleton(new ImageOptions(MaximumEdge: ArtworkSizing.MaximumEdge));
        services.AddSingleton<IImagePipeline, ImagePipeline>();
        services.AddSingleton<ICatalogPresentationCache, IsolatedCatalogPresentationCache>();
        services.AddSingleton<Func<IArtworkTextDetector>>(_ => () => new WindowsArtworkTextDetector());
        if (!fixtureOnline && !performanceImages)
            services.AddSingleton<IPublicArtworkSource>(p => new PublicArtworkSources(
                new VndbArtworkSource(p.GetRequiredService<PublicArtworkNetwork>().Client,
                    () => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "data", "vndb-banner-hints.json"))),
                new SteamArtworkSource(p.GetRequiredService<PublicArtworkNetwork>().Client, p.GetRequiredService<IImagePipeline>(), p.GetRequiredService<IRemoteCoverRegistry>())));
        services.AddSingleton<CatalogPresentationService>();
        services.AddSingleton<ICoverRecommendationSource>(p => p.GetRequiredService<CatalogPresentationService>());
        services.AddSingleton<ICachedCoverRecommendationSource>(p => p.GetRequiredService<CatalogPresentationService>());
        services.AddSingleton<FilesViewModel>();
        if (nativeConfiguration.Diagnostics)
        {
            services.AddSingleton(new SyntheticScenario(configuration["HOLD_PREFERENCES"] == "1", configuration["HOLD_IMAGES"] == "1"));
            services.AddSingleton<IOfflineFilesClient>(p => p.GetRequiredService<SyntheticScenario>().Wrap(p.GetRequiredService<OfflineFilesClient>()));
            services.AddSingleton<IImageSourceProvider>(p => p.GetRequiredService<SyntheticScenario>().Wrap(p.GetRequiredService<CombinedCoverSources>()));
            services.AddSingleton<IClientTextRules>(p => p.GetRequiredService<SyntheticScenario>().Wrap(new LegacyClientTextRules()));
            services.AddSingleton<IUiPreferencesStore>(p => p.GetRequiredService<SyntheticScenario>().Wrap(new IsolatedUiPreferencesStore(nativeConfiguration)));
            services.AddSingleton<IOfflineLibraryClient>(p => p.GetRequiredService<SyntheticScenario>().Wrap(p.GetRequiredService<OfflineLibraryClient>()));
        }
        else
        {
            services.AddSingleton<IOfflineFilesClient>(p => p.GetRequiredService<OfflineFilesClient>());
            services.AddSingleton<IImageSourceProvider>(p => p.GetRequiredService<CombinedCoverSources>());
            services.AddSingleton<IClientTextRules, LegacyClientTextRules>();
            services.AddSingleton<IUiPreferencesStore, IsolatedUiPreferencesStore>();
            services.AddSingleton<IOfflineLibraryClient>(p => p.GetRequiredService<OfflineLibraryClient>());
        }
        services.AddSingleton<WorkspaceViewModel>();
        services.AddSingleton<MainWindow>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        // Resolution is confined to this composition root. Views and view models receive constructors.
        return new(provider, provider.GetRequiredService<MainWindow>(), provider.GetRequiredService<AppSessionHost>(), provider.GetRequiredService<IAppUpdateInstaller>(), provider.GetRequiredService<ILogger<NativeHost>>());
    }
}

internal sealed class NativeHost(ServiceProvider provider, MainWindow window, AppSessionHost sessions, IAppUpdateInstaller updates, ILogger<NativeHost> logger) : IAsyncDisposable
{
    public MainWindow Window { get; } = window;
    public AppSessionHost Sessions { get; } = sessions;
    public IAppUpdateInstaller Updates { get; } = updates;
    public void LogFatal(Exception exception) => logger.LogCritical(exception, "Native client terminated: {ExceptionType}", exception.GetType().Name);
    public void LogStarted() => logger.LogInformation("Native client started");
    public async ValueTask DisposeAsync() { await Window.StopAsync(); await Sessions.DisposeAsync(); logger.LogInformation("Native client drained"); await provider.DisposeAsync(); }
}
