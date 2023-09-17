using JPPhotoManager.Domain;
using JPPhotoManager.Domain.Interfaces;
using log4net;
using Microsoft.EntityFrameworkCore;
using SimplePortableDatabase;
using System.Data;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace JPPhotoManager.Infrastructure
{
    public class SqliteAssetRepository : IAssetRepository
    {
        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private const double STORAGE_VERSION = 1.1;
        private const string SEPARATOR = "|";
        private const int PAGE_SIZE = 100;

        public bool IsInitialized { get; private set; }
        private string _dataDirectory;
        private readonly AppDbContext _appDbContext;
        private readonly IDatabase _database;
        private readonly IStorageService _storageService;
        private readonly IUserConfigurationService _userConfigurationService;

        private SyncAssetsConfiguration _syncAssetsConfiguration;
        private List<string> _recentTargetPaths;
        protected Dictionary<string, byte[]> Thumbnails { get; private set; }
        private Queue<string> _recentThumbnailsQueue;
        private bool _hasChanges;
        private object _syncLock;

        public SqliteAssetRepository(AppDbContext appDbContext, IDatabase database, IStorageService storageService, IUserConfigurationService userConfigurationService)
        {
            _appDbContext = appDbContext;
            _database = database;
            _storageService = storageService;
            _userConfigurationService = userConfigurationService;
            Thumbnails = new Dictionary<string, byte[]>();
            _recentThumbnailsQueue = new Queue<string>();
            _syncLock = new object();
            Initialize();
        }

        private void Initialize()
        {
            if (!IsInitialized)
            {
                InitializeDatabase();
                IsInitialized = true;
            }
        }

        private void InitializeDatabase()
        {
            _appDbContext.Database.EnsureCreated();
            _dataDirectory = _storageService.ResolveDataDirectory(STORAGE_VERSION);
            var separatorChar = SEPARATOR.ToCharArray().First();
            _database.Initialize(_dataDirectory, separatorChar);
        }

        public bool ShouldWriteBackup(DateTime today)
        {
            // TODO: Implement for Sqlite file.
            //bool shouldWrite = false;
            //var days = _userConfigurationService.GetBackupEveryNDays();

            //if (days > 0)
            //{
            //    var backupDates = _database.GetBackupDates();

            //    if (backupDates?.Length > 0)
            //    {
            //        var lastBackupDate = backupDates.Max();
            //        var newBackupDate = lastBackupDate.AddDays(days);

            //        shouldWrite = today.Date >= newBackupDate.Date
            //            && !_database.BackupExists(today.Date);
            //    }
            //    else
            //    {
            //        shouldWrite = !_database.BackupExists(today.Date);
            //    }
            //}

            //return shouldWrite;
            return true;
        }

        public void WriteBackup()
        {
            //if (_database.WriteBackup(DateTime.Now.Date))
            //{
            //    _database.DeleteOldBackups(_userConfigurationService.GetBackupsToKeep());
            //}
        }

        public List<Folder> ReadFolders()
        {
            List<Folder> result;

            try
            {
                result = _appDbContext.Folders.ToList();
            }
            catch (ArgumentException ex)
            {
                throw new ApplicationException($"Error while trying to read data table 'Folder'. " +
                    $"DataDirectory: {_database.DataDirectory} - " +
                    $"Separator: {_database.Separator} - " +
                    $"LastReadFilePath: {_database.Diagnostics.LastReadFilePath} - " +
                    $"LastReadFileRaw: {_database.Diagnostics.LastReadFileRaw}",
                    ex);
            }

            return result;
        }

        public List<Asset> ReadAssets()
        {
            List<Asset> result;

            try
            {
                result = _appDbContext
                    .Assets
                    .Include(a => a.Folder)
                    .ToList();
            }
            catch (ArgumentException ex)
            {
                throw new ApplicationException($"Error while trying to read data table 'Asset'. " +
                    $"DataDirectory: {_database.DataDirectory} - " +
                    $"Separator: {_database.Separator} - " +
                    $"LastReadFilePath: {_database.Diagnostics.LastReadFilePath} - " +
                    $"LastReadFileRaw: {_database.Diagnostics.LastReadFileRaw}",
                    ex);
            }

            return result;
        }

        public List<SyncAssetsDirectoriesDefinition> ReadSyncDefinitions()
        {
            List<SyncAssetsDirectoriesDefinition> result;

            try
            {
                result = _appDbContext.SyncAssetsDirectoriesDefinitions.ToList();
            }
            catch (ArgumentException ex)
            {
                throw new ApplicationException($"Error while trying to read data table 'Import'. " +
                    $"DataDirectory: {_database.DataDirectory} - " +
                    $"Separator: {_database.Separator} - " +
                    $"LastReadFilePath: {_database.Diagnostics.LastReadFilePath} - " +
                    $"LastReadFileRaw: {_database.Diagnostics.LastReadFileRaw}",
                    ex);
            }

            return result;
        }

        public List<string> ReadRecentTargetPaths()
        {
            // TODO: Implement as an entity.
            //List<string> result;

            //try
            //{
            //    result = _database.ReadObjectList("RecentTargetPaths", f => f[0]);
            //}
            //catch (ArgumentException ex)
            //{
            //    throw new ApplicationException($"Error while trying to read data table 'RecentTargetPaths'. " +
            //        $"DataDirectory: {_database.DataDirectory} - " +
            //        $"Separator: {_database.Separator} - " +
            //        $"LastReadFilePath: {_database.Diagnostics.LastReadFilePath} - " +
            //        $"LastReadFileRaw: {_database.Diagnostics.LastReadFileRaw}",
            //        ex);
            //}

            //return result;
            return null;
        }

        public void WriteRecentTargetPaths(List<string> recentTargetPaths)
        {
            // TODO: Implement as an entity.
            //_database.WriteObjectList(recentTargetPaths, "RecentTargetPaths", (p, i) =>
            //{
            //    return i switch
            //    {
            //        0 => p,
            //        _ => throw new ArgumentOutOfRangeException(nameof(i))
            //    };
            //});
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
            string thumbnailsFilePath = _database.ResolveBlobFilePath(_dataDirectory, asset.ThumbnailBlobName);
            File.Delete(thumbnailsFilePath);
        }

        public void DeleteThumbnail(string thumbnailBlobName)
        {
            if (Thumbnails.ContainsKey(thumbnailBlobName))
            {
                Thumbnails.Remove(thumbnailBlobName);
            }

            // TODO: Implement through the NuGet package.
            string thumbnailsFilePath = _database.ResolveBlobFilePath(_dataDirectory, thumbnailBlobName);
            File.Delete(thumbnailsFilePath);
        }

        public string[] GetThumbnailsList()
        {
            string blobsDirectory = _database.GetBlobsDirectory(_dataDirectory);
            return Directory.GetFiles(blobsDirectory);
        }

        private void RemoveOldThumbnailsDictionaryEntries(Folder folder)
        {
            int entriesToKeep = _userConfigurationService.GetThumbnailsDictionaryEntriesToKeep();

            if (!_recentThumbnailsQueue.Contains(folder.Path))
            {
                _recentThumbnailsQueue.Enqueue(folder.Path);
            }

            if (_recentThumbnailsQueue.Count > entriesToKeep)
            {
                var pathToRemove = _recentThumbnailsQueue.Dequeue();
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

            lock (_syncLock)
            {
                result = _hasChanges;
            }

            return result;
        }

        private void SaveThumbnails(Dictionary<string, byte[]> thumbnails, string thumbnailsFileName)
        {
            _database.WriteBlob(thumbnails, thumbnailsFileName);
        }

        public PaginatedData<Asset> GetAssets(string directory, int pageIndex)
        {
            PaginatedData<Asset> result;
            List<Asset> assetsList = null;
            bool isNewFile = false;
            int totalCount = 0;

            try
            {
                lock (_syncLock)
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
                                    var bytes = (byte[])_database.ReadBlob(asset.ThumbnailBlobName);

                                    if (bytes != null)
                                    {
                                        Thumbnails[asset.ThumbnailBlobName] = bytes;
                                    }
                                }
                                
                                asset.ImageData = Thumbnails.ContainsKey(asset.ThumbnailBlobName) ? _storageService.LoadBitmapImage(Thumbnails[asset.ThumbnailBlobName], asset.ThumbnailPixelWidth, asset.ThumbnailPixelHeight) : null;
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
                            _storageService.GetFileInformation(asset);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex);
            }

            return new PaginatedData<Asset> { Items = assetsList.ToArray(), PageIndex = pageIndex, TotalCount = totalCount };
        }

        public bool FolderExists(string path)
        {
            bool result = false;

            lock (_syncLock)
            {
                result = _appDbContext.Folders.Any(f => f.Path == path);
            }

            return result;
        }

        public Folder AddFolder(string path)
        {
            Folder folder;

            lock (_syncLock)
            {
                string folderId = Guid.NewGuid().ToString();

                folder = new Folder
                {
                    FolderId = folderId,
                    Path = path
                };

                _hasChanges = true;

                _appDbContext.Folders.Add(folder);
                _appDbContext.SaveChanges();
            }

            return folder;
        }

        public void AddAsset(Asset asset, byte[] thumbnailData)
        {
            lock (_syncLock)
            {
                Folder folder = GetFolderById(asset.FolderId);

                if (folder == null)
                {
                    AddFolder(asset.Folder.Path);
                }

                Thumbnails[asset.ThumbnailBlobName] = thumbnailData;

                if (thumbnailData != null)
                {
                    _database.WriteBlob(thumbnailData, asset.ThumbnailBlobName);
                }

                _hasChanges = true;

                _appDbContext.Assets.Add(asset);
                _appDbContext.SaveChanges();
            }
        }

        public Folder[] GetFolders()
        {
            Folder[] result;

            lock (_syncLock)
            {
                result = _appDbContext.Folders.ToArray();
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

            lock (_syncLock)
            {
                result = _appDbContext.Folders.FirstOrDefault(f => f.Path == path);
            }

            return result;
        }

        private Folder GetFolderById(string folderId)
        {
            Folder result = null;

            lock (_syncLock)
            {
                result = _appDbContext.Folders.FirstOrDefault(f => f.FolderId == folderId);
            }

            return result;
        }

        private List<Asset> GetAssetsByFolderId(string folderId)
        {
            List<Asset> result = null;

            lock (_syncLock)
            {
                result = _appDbContext.Assets.Where(a => a.FolderId == folderId).ToList();
            }

            return result;
        }

        private Asset GetAssetByFolderIdFileName(string folderId, string fileName)
        {
            Asset result = null;

            lock (_syncLock)
            {
                result = _appDbContext.Assets.FirstOrDefault(a => a.FolderId == folderId && a.FileName == fileName);
            }

            return result;
        }

        public void DeleteAsset(string directory, string fileName)
        {
            lock (_syncLock)
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

                        _appDbContext.Assets.Remove(deletedAsset);
                        _appDbContext.SaveChanges();
                        _hasChanges = true;
                    }
                }
            }
        }

        public void DeleteFolder(Folder folder)
        {
            lock (_syncLock)
            {
                if (folder != null)
                {
                    DeleteThumbnails(folder);

                    _appDbContext.Folders.Remove(folder);
                    _appDbContext.SaveChanges();
                    _hasChanges = true;
                }
            }
        }

        public List<Asset> GetCataloguedAssets()
        {
            List<Asset> cataloguedAssets = null;

            lock (_syncLock)
            {
                cataloguedAssets = _appDbContext.Assets.ToList();
            }

            return cataloguedAssets;
        }

        public List<Asset> GetCataloguedAssets(string directory)
        {
            List<Asset> cataloguedAssets = null;

            lock (_syncLock)
            {
                Folder folder = GetFolderByPath(directory);

                if (folder != null)
                {
                    cataloguedAssets = _appDbContext.Assets.Where(a => a.FolderId == folder.FolderId).ToList();
                }
            }

            return cataloguedAssets;
        }

        public bool IsAssetCatalogued(string directoryName, string fileName)
        {
            bool result = false;

            lock (_syncLock)
            {
                Folder folder = GetFolderByPath(directoryName);
                result = folder != null && GetAssetByFolderIdFileName(folder.FolderId, fileName) != null;
            }

            return result;
        }

        public bool ContainsThumbnail(string directoryName, string fileName)
        {
            bool result = false;

            lock (_syncLock)
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
                            var thumbnail = (byte[])_database.ReadBlob(thumbnailBlobName);

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

            lock (_syncLock)
            {
                var folder = GetFolderByPath(directoryName);
                var asset = GetAssetByFolderIdFileName(folder.FolderId, fileName);
                var thumbnailBlobName = asset.ThumbnailBlobName;

                if (!Thumbnails.ContainsKey(thumbnailBlobName))
                {
                    var thumbnail = (byte[])_database.ReadBlob(thumbnailBlobName);

                    if (thumbnail != null)
                    {
                        Thumbnails[thumbnailBlobName] = thumbnail;
                    }
                }

                if (Thumbnails.ContainsKey(thumbnailBlobName))
                {
                    result = _storageService.LoadBitmapImage(Thumbnails[thumbnailBlobName], width, height);
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

            lock (_syncLock)
            {
                result = _syncAssetsConfiguration;
            }

            return result;
        }

        public void SaveSyncAssetsConfiguration(SyncAssetsConfiguration syncAssetsConfiguration)
        {
            lock (_syncLock)
            {
                this._syncAssetsConfiguration = syncAssetsConfiguration;
                _hasChanges = true;
            }
        }

        public List<string> GetRecentTargetPaths()
        {
            List<string> result = null;

            lock (_syncLock)
            {
                result = _recentTargetPaths;
            }

            return result;
        }

        public void SaveRecentTargetPaths(List<string> recentTargetPaths)
        {
            lock (_syncLock)
            {
                this._recentTargetPaths = recentTargetPaths;
                _hasChanges = true;
            }
        }

        public void SaveCatalog(Folder folder)
        {
            // Not needed for SQLite.
        }
    }
}