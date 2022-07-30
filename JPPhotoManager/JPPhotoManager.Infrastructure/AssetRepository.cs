using JPPhotoManager.Domain;
using JPPhotoManager.Domain.Interfaces;
using log4net;
using SimplePortableDatabase;
using System.Data;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace JPPhotoManager.Infrastructure
{
    public class AssetRepository : IAssetRepository
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private const double STORAGE_VERSION = 1.1;
        private const string SEPARATOR = "|";
        private const int PAGE_SIZE = 100;

        public bool IsInitialized { get; private set; }
        private string dataDirectory;
        private readonly IDatabase database;
        private readonly IStorageService storageService;
        private readonly IUserConfigurationService userConfigurationService;

        private List<Asset> assets;
        private List<Folder> folders;
        private SyncAssetsConfiguration syncAssetsConfiguration;
        private List<string> recentTargetPaths;
        protected Dictionary<string, byte[]> Thumbnails { get; private set; }
        private Queue<string> recentThumbnailsQueue;
        private bool hasChanges;
        private object syncLock;

        public AssetRepository(IDatabase database, IStorageService storageService, IUserConfigurationService userConfigurationService)
        {
            this.database = database;
            this.storageService = storageService;
            this.userConfigurationService = userConfigurationService;
            Thumbnails = new Dictionary<string, byte[]>();
            recentThumbnailsQueue = new Queue<string>();
            syncLock = new object();
            Initialize();
        }

        private void Initialize()
        {
            if (!IsInitialized)
            {
                InitializeDatabase();
                ReadCatalog();

                if (assets == null)
                {
                    SaveCatalog(null);
                }

                IsInitialized = true;
            }
        }

        private void InitializeDatabase()
        {
            dataDirectory = storageService.ResolveDataDirectory(STORAGE_VERSION);
            var separatorChar = SEPARATOR.ToCharArray().First();
            database.Initialize(dataDirectory, separatorChar);

            database.SetDataTableProperties(new DataTableProperties
            {
                TableName = "Folder",
                ColumnProperties = new ColumnProperties[]
                {
                    new ColumnProperties { ColumnName = "FolderId" },
                    new ColumnProperties { ColumnName = "Path" }
                }
            });

            database.SetDataTableProperties(new DataTableProperties
            {
                TableName = "Asset",
                ColumnProperties = new ColumnProperties[]
                {
                    new ColumnProperties { ColumnName = "AssetId" },
                    new ColumnProperties { ColumnName = "FolderId" },
                    new ColumnProperties { ColumnName = "FileName" },
                    new ColumnProperties { ColumnName = "FileSize" },
                    new ColumnProperties { ColumnName = "ImageRotation" },
                    new ColumnProperties { ColumnName = "PixelWidth" },
                    new ColumnProperties { ColumnName = "PixelHeight" },
                    new ColumnProperties { ColumnName = "ThumbnailPixelWidth" },
                    new ColumnProperties { ColumnName = "ThumbnailPixelHeight" },
                    new ColumnProperties { ColumnName = "ThumbnailCreationDateTime" },
                    new ColumnProperties { ColumnName = "Hash" }
                }
            });

            database.SetDataTableProperties(new DataTableProperties
            {
                TableName = "Import",
                ColumnProperties = new ColumnProperties[]
                {
                    new ColumnProperties { ColumnName = "SourceDirectory" },
                    new ColumnProperties { ColumnName = "DestinationDirectory" },
                    new ColumnProperties { ColumnName = "IncludeSubFolders" },
                    new ColumnProperties { ColumnName = "DeleteAssetsNotInSource" }
                }
            });

            database.SetDataTableProperties(new DataTableProperties
            {
                TableName = "RecentTargetPaths",
                ColumnProperties = new ColumnProperties[]
                {
                    new ColumnProperties { ColumnName = "Path" }
                }
            });
        }

        private void ReadCatalog()
        {
            assets = ReadAssets();
            folders = ReadFolders();
            syncAssetsConfiguration = new SyncAssetsConfiguration();

            var syncDefinitions = ReadSyncDefinitions();
            
            if (syncDefinitions != null)
            {
                syncAssetsConfiguration.Definitions.AddRange(syncDefinitions);
            }
            
            assets?.ForEach(a => a.Folder = GetFolderById(a.FolderId));
            recentTargetPaths = ReadRecentTargetPaths();
        }

        public void SaveCatalog(Folder folder)
        {
            lock (syncLock)
            {
                if (hasChanges)
                {
                    WriteAssets(assets);
                    WriteFolders(folders);
                    WriteSyncDefinitions(syncAssetsConfiguration.Definitions);
                    WriteRecentTargetPaths(recentTargetPaths);
                }

                hasChanges = false;
            }
        }

        public bool ShouldWriteBackup(DateTime today)
        {
            bool shouldWrite = false;
            var days = userConfigurationService.GetBackupEveryNDays();

            if (days > 0)
            {
                var backupDates = database.GetBackupDates();

                if (backupDates?.Length > 0)
                {
                    var lastBackupDate = backupDates.Max();
                    var newBackupDate = lastBackupDate.AddDays(days);

                    shouldWrite = today.Date >= newBackupDate.Date
                        && !database.BackupExists(today.Date);
                }
                else
                {
                    shouldWrite = !database.BackupExists(today.Date);
                }
            }

            return shouldWrite;
        }

        public void WriteBackup()
        {
            if (database.WriteBackup(DateTime.Now.Date))
            {
                database.DeleteOldBackups(userConfigurationService.GetBackupsToKeep());
            }
        }

        public List<Folder> ReadFolders()
        {
            List<Folder> result;

            try
            {
                result = database.ReadObjectList("Folder", f =>
                    new Folder
                    {
                        FolderId = f[0],
                        Path = f[1]
                    });
            }
            catch (ArgumentException ex)
            {
                throw new ApplicationException($"Error while trying to read data table 'Folder'. " +
                    $"DataDirectory: {database.DataDirectory} - " +
                    $"Separator: {database.Separator} - " +
                    $"LastReadFilePath: {database.Diagnostics.LastReadFilePath} - " +
                    $"LastReadFileRaw: {database.Diagnostics.LastReadFileRaw}",
                    ex);
            }

            return result;
        }

        public List<Asset> ReadAssets()
        {
            List<Asset> result;

            try
            {
                result = database.ReadObjectList("Asset", f =>
                    new Asset
                    {
                        AssetId = f[0],
                        FolderId = f[1],
                        FileName = f[2],
                        FileSize = long.Parse(f[3]),
                        ImageRotation = (Rotation)Enum.Parse(typeof(Rotation), f[4]),
                        PixelWidth = int.Parse(f[5]),
                        PixelHeight = int.Parse(f[6]),
                        ThumbnailPixelWidth = int.Parse(f[7]),
                        ThumbnailPixelHeight = int.Parse(f[8]),
                        ThumbnailCreationDateTime = DateTime.Parse(f[9]),
                        Hash = f[10]
                    });
            }
            catch (ArgumentException ex)
            {
                throw new ApplicationException($"Error while trying to read data table 'Asset'. " +
                    $"DataDirectory: {database.DataDirectory} - " +
                    $"Separator: {database.Separator} - " +
                    $"LastReadFilePath: {database.Diagnostics.LastReadFilePath} - " +
                    $"LastReadFileRaw: {database.Diagnostics.LastReadFileRaw}",
                    ex);
            }

            return result;
        }

        public List<SyncAssetsDirectoriesDefinition> ReadSyncDefinitions()
        {
            List<SyncAssetsDirectoriesDefinition> result;

            try
            {
                result = database.ReadObjectList("Import", f =>
                    new SyncAssetsDirectoriesDefinition
                    {
                        SourceDirectory = f[0],
                        DestinationDirectory = f[1],
                        IncludeSubFolders = bool.Parse(f[2]),
                        DeleteAssetsNotInSource = f.Length > 3 && bool.Parse(f[3])
                    });
            }
            catch (ArgumentException ex)
            {
                throw new ApplicationException($"Error while trying to read data table 'Import'. " +
                    $"DataDirectory: {database.DataDirectory} - " +
                    $"Separator: {database.Separator} - " +
                    $"LastReadFilePath: {database.Diagnostics.LastReadFilePath} - " +
                    $"LastReadFileRaw: {database.Diagnostics.LastReadFileRaw}",
                    ex);
            }

            return result;
        }

        public List<string> ReadRecentTargetPaths()
        {
            List<string> result;

            try
            {
                result = database.ReadObjectList("RecentTargetPaths", f => f[0]);
            }
            catch (ArgumentException ex)
            {
                throw new ApplicationException($"Error while trying to read data table 'RecentTargetPaths'. " +
                    $"DataDirectory: {database.DataDirectory} - " +
                    $"Separator: {database.Separator} - " +
                    $"LastReadFilePath: {database.Diagnostics.LastReadFilePath} - " +
                    $"LastReadFileRaw: {database.Diagnostics.LastReadFileRaw}",
                    ex);
            }

            return result;
        }

        public void WriteFolders(List<Folder> folders)
        {
            database.WriteObjectList(folders, "Folder", (f, i) =>
            {
                return i switch
                {
                    0 => f.FolderId,
                    1 => f.Path,
                    _ => throw new ArgumentOutOfRangeException(nameof(i))
                };
            });
        }

        public void WriteAssets(List<Asset> assets)
        {
            database.WriteObjectList(assets, "Asset", (a, i) =>
            {
                return i switch
                {
                    0 => a.AssetId,
                    1 => a.FolderId,
                    2 => a.FileName,
                    3 => a.FileSize,
                    4 => a.ImageRotation,
                    5 => a.PixelWidth,
                    6 => a.PixelHeight,
                    7 => a.ThumbnailPixelWidth,
                    8 => a.ThumbnailPixelHeight,
                    9 => a.ThumbnailCreationDateTime,
                    10 => a.Hash,
                    _ => throw new ArgumentOutOfRangeException(nameof(i))
                };
            });
        }

        public void WriteSyncDefinitions(List<SyncAssetsDirectoriesDefinition> definitions)
        {
            database.WriteObjectList(definitions, "Import", (d, i) =>
            {
                return i switch
                {
                    0 => d.SourceDirectory,
                    1 => d.DestinationDirectory,
                    2 => d.IncludeSubFolders,
                    3 => d.DeleteAssetsNotInSource,
                    _ => throw new ArgumentOutOfRangeException(nameof(i))
                };
            });
        }

        public void WriteRecentTargetPaths(List<string> recentTargetPaths)
        {
            database.WriteObjectList(recentTargetPaths, "RecentTargetPaths", (p, i) =>
            {
                return i switch
                {
                    0 => p,
                    _ => throw new ArgumentOutOfRangeException(nameof(i))
                };
            });
        }

        private void DeleteThumbnails(Folder folder)
        {
            var assets = GetAssetsByFolderId(folder.FolderId);

            foreach (var asset in assets)
            {
                DeleteThumbnail(asset);
            }
        }

        protected void DeleteThumbnail(Asset asset)
        {
            if (Thumbnails.ContainsKey(asset.ThumbnailBlobName))
            {
                Thumbnails.Remove(asset.ThumbnailBlobName);
            }

            // TODO: Implement through the NuGet package.
            string thumbnailsFilePath = database.ResolveBlobFilePath(dataDirectory, asset.ThumbnailBlobName);
            File.Delete(thumbnailsFilePath);
        }

        private void RemoveOldThumbnailsDictionaryEntries(Folder folder)
        {
            int entriesToKeep = userConfigurationService.GetThumbnailsDictionaryEntriesToKeep();

            if (!recentThumbnailsQueue.Contains(folder.Path))
            {
                recentThumbnailsQueue.Enqueue(folder.Path);
            }

            if (recentThumbnailsQueue.Count > entriesToKeep)
            {
                var pathToRemove = recentThumbnailsQueue.Dequeue();
                var folderToRemove = GetFolderByPath(pathToRemove);
                var assets = GetAssetsByFolderId(folderToRemove.FolderId);

                foreach (var asset in assets)
                {
                    if (Thumbnails.ContainsKey(asset.ThumbnailBlobName))
                    {
                        Thumbnails.Remove(asset.ThumbnailBlobName);
                    }
                }
            }
        }

        public bool HasChanges()
        {
            bool result = false;

            lock (syncLock)
            {
                result = hasChanges;
            }

            return result;
        }

        private void SaveThumbnails(Dictionary<string, byte[]> thumbnails, string thumbnailsFileName)
        {
            database.WriteBlob(thumbnails, thumbnailsFileName);
        }

        public PaginatedData<Asset> GetAssets(string directory, int pageIndex)
        {
            PaginatedData<Asset> result;
            List<Asset> assetsList = null;
            bool isNewFile = false;
            int totalCount = 0;

            try
            {
                lock (syncLock)
                {
                    Folder folder = GetFolderByPath(directory);

                    if (folder != null)
                    {
                        assetsList = GetAssetsByFolderId(folder.FolderId);
                        totalCount = assetsList.Count;
                        assetsList = assetsList.Skip(pageIndex * PAGE_SIZE).Take(PAGE_SIZE).ToList();
                        
                        RemoveOldThumbnailsDictionaryEntries(folder);

                        if (!isNewFile)
                        {
                            foreach (Asset asset in assetsList)
                            {
                                if (!Thumbnails.ContainsKey(asset.ThumbnailBlobName))
                                {
                                    var bytes = (byte[])database.ReadBlob(asset.ThumbnailBlobName);

                                    if (bytes != null)
                                    {
                                        Thumbnails[asset.ThumbnailBlobName] = bytes;
                                    }
                                }
                                
                                asset.ImageData = Thumbnails.ContainsKey(asset.ThumbnailBlobName) ? storageService.LoadBitmapImage(Thumbnails[asset.ThumbnailBlobName], asset.ThumbnailPixelWidth, asset.ThumbnailPixelHeight) : null;
                            }

                            // Removes assets with no thumbnails.
                            List<Asset> assetsToRemove = new();

                            for (int i = 0; i < assetsList.Count; i++)
                            {
                                if (assetsList[i].ImageData == null)
                                {
                                    assetsToRemove.Add(assetsList[i]);
                                }
                            }

                            foreach (Asset asset in assetsToRemove)
                            {
                                assetsList.Remove(asset);
                            }
                        }

                        foreach (Asset asset in assetsList)
                        {
                            storageService.GetFileInformation(asset);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }

            return new PaginatedData<Asset> { Items = assetsList.ToArray(), PageIndex = pageIndex, TotalCount = totalCount };
        }

        public bool FolderExists(string path)
        {
            bool result = false;

            lock (syncLock)
            {
                result = folders.Any(f => f.Path == path);
            }

            return result;
        }

        public Folder AddFolder(string path)
        {
            Folder folder;

            lock (syncLock)
            {
                string folderId = Guid.NewGuid().ToString();

                folder = new Folder
                {
                    FolderId = folderId,
                    Path = path
                };

                folders.Add(folder);
                hasChanges = true;
            }

            return folder;
        }

        public void AddAsset(Asset asset, byte[] thumbnailData)
        {
            lock (syncLock)
            {
                Folder folder = GetFolderById(asset.FolderId);

                if (folder == null)
                {
                    AddFolder(asset.Folder.Path);
                }

                Thumbnails[asset.ThumbnailBlobName] = thumbnailData;

                if (thumbnailData != null)
                {
                    database.WriteBlob(thumbnailData, asset.ThumbnailBlobName);
                }

                assets.Add(asset);
                hasChanges = true;
            }
        }

        public Folder[] GetFolders()
        {
            Folder[] result;

            lock (syncLock)
            {
                result = folders.ToArray();
            }

            return result;
        }

        public Folder[] GetSubFolders(Folder parentFolder, bool includeHidden)
        {
            Folder[] folders = GetFolders();
            folders = folders.Where(f => parentFolder.IsParentOf(f)).ToArray();
            return folders;
        }

        public Folder GetFolderByPath(string path)
        {
            Folder result = null;

            lock (syncLock)
            {
                result = folders.FirstOrDefault(f => f.Path == path);
            }

            return result;
        }

        private Folder GetFolderById(string folderId)
        {
            Folder result = null;

            lock (syncLock)
            {
                result = folders.FirstOrDefault(f => f.FolderId == folderId);
            }

            return result;
        }

        private List<Asset> GetAssetsByFolderId(string folderId)
        {
            List<Asset> result = null;

            lock (syncLock)
            {
                result = assets.Where(a => a.FolderId == folderId).ToList();
            }

            return result;
        }

        private Asset GetAssetByFolderIdFileName(string folderId, string fileName)
        {
            Asset result = null;

            lock (syncLock)
            {
                result = assets.FirstOrDefault(a => a.FolderId == folderId && a.FileName == fileName);
            }

            return result;
        }

        public void DeleteAsset(string directory, string fileName)
        {
            lock (syncLock)
            {
                Folder folder = GetFolderByPath(directory);

                if (folder != null)
                {
                    Asset deletedAsset = GetAssetByFolderIdFileName(folder.FolderId, fileName);

                    if (deletedAsset != null)
                    {
                        if (Thumbnails.ContainsKey(deletedAsset.ThumbnailBlobName))
                        {
                            DeleteThumbnail(deletedAsset);
                        }

                        assets.Remove(deletedAsset);
                        hasChanges = true;
                    }
                }
            }
        }

        public void DeleteFolder(Folder folder)
        {
            lock (syncLock)
            {
                if (folder != null)
                {
                    DeleteThumbnails(folder);

                    folders.Remove(folder);
                    hasChanges = true;
                }
            }
        }

        public List<Asset> GetCataloguedAssets()
        {
            List<Asset> cataloguedAssets = null;

            lock (syncLock)
            {
                cataloguedAssets = assets;
            }

            return cataloguedAssets;
        }

        public List<Asset> GetCataloguedAssets(string directory)
        {
            List<Asset> cataloguedAssets = null;

            lock (syncLock)
            {
                Folder folder = GetFolderByPath(directory);

                if (folder != null)
                {
                    cataloguedAssets = assets.Where(a => a.FolderId == folder.FolderId).ToList();
                }
            }

            return cataloguedAssets;
        }

        public bool IsAssetCatalogued(string directoryName, string fileName)
        {
            bool result = false;

            lock (syncLock)
            {
                Folder folder = GetFolderByPath(directoryName);
                result = folder != null && GetAssetByFolderIdFileName(folder.FolderId, fileName) != null;
            }

            return result;
        }

        public bool ContainsThumbnail(string directoryName, string fileName)
        {
            bool result = false;

            lock (syncLock)
            {
                var folder = GetFolderByPath(directoryName);

                if (folder != null)
                {
                    var asset = GetAssetByFolderIdFileName(folder.FolderId, fileName);

                    if (asset != null)
                    {
                        var thumbnailBlobName = asset.ThumbnailBlobName;

                        if (!Thumbnails.ContainsKey(thumbnailBlobName))
                        {
                            var thumbnail = (byte[])database.ReadBlob(thumbnailBlobName);

                            if (thumbnail != null)
                            {
                                Thumbnails[thumbnailBlobName] = thumbnail;
                            }
                        }

                        result = Thumbnails.ContainsKey(thumbnailBlobName);
                    }
                }
            }

            return result;
        }

        public BitmapImage LoadThumbnail(string directoryName, string fileName, int width, int height)
        {
            BitmapImage result = null;

            lock (syncLock)
            {
                var folder = GetFolderByPath(directoryName);
                var asset = GetAssetByFolderIdFileName(folder.FolderId, fileName);
                var thumbnailBlobName = asset.ThumbnailBlobName;

                if (!Thumbnails.ContainsKey(thumbnailBlobName))
                {
                    var thumbnail = (byte[])database.ReadBlob(thumbnailBlobName);

                    if (thumbnail != null)
                    {
                        Thumbnails[thumbnailBlobName] = thumbnail;
                    }
                }

                if (Thumbnails.ContainsKey(thumbnailBlobName))
                {
                    result = storageService.LoadBitmapImage(Thumbnails[thumbnailBlobName], width, height);
                }
                else
                {
                    DeleteAsset(directoryName, fileName);
                    SaveCatalog(folder);
                }
            }

            return result;
        }

        public SyncAssetsConfiguration GetSyncAssetsConfiguration()
        {
            SyncAssetsConfiguration result;

            lock (syncLock)
            {
                result = syncAssetsConfiguration;
            }

            return result;
        }

        public void SaveSyncAssetsConfiguration(SyncAssetsConfiguration syncAssetsConfiguration)
        {
            lock (syncLock)
            {
                this.syncAssetsConfiguration = syncAssetsConfiguration;
                hasChanges = true;
            }
        }

        public List<string> GetRecentTargetPaths()
        {
            List<string> result = null;

            lock (syncLock)
            {
                result = recentTargetPaths;
            }

            return result;
        }

        public void SaveRecentTargetPaths(List<string> recentTargetPaths)
        {
            lock (syncLock)
            {
                this.recentTargetPaths = recentTargetPaths;
                hasChanges = true;
            }
        }
    }
}
