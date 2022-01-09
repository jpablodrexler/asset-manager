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

        private const int STORAGE_VERSION = 3;
        private const string SEPARATOR = "|";

        public bool IsInitialized { get; private set; }
        private string dataDirectory;
        private readonly IDatabase database;
        private readonly IStorageService storageService;
        private readonly IUserConfigurationService userConfigurationService;
        protected AssetCatalog AssetCatalog { get; private set; }
        protected Dictionary<string, Dictionary<string, byte[]>> Thumbnails { get; private set; }

        public AssetRepository(IDatabase database, IStorageService storageService, IUserConfigurationService userConfigurationService)
        {
            this.database = database;
            this.storageService = storageService;
            this.userConfigurationService = userConfigurationService;
            Thumbnails = new Dictionary<string, Dictionary<string, byte[]>>();
            Initialize();
        }

        private void Initialize()
        {
            if (!IsInitialized)
            {
                InitializeDatabase();
                ReadCatalog();

                if (AssetCatalog == null)
                {
                    AssetCatalog = new AssetCatalog();
                    AssetCatalog.StorageVersion = STORAGE_VERSION;
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
                    new ColumnProperties { ColumnName = "IncludeSubFolders" }
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
            AssetCatalog = new AssetCatalog();
            AssetCatalog.Assets.Clear();
            AssetCatalog.Assets.AddRange(ReadAssets());
            AssetCatalog.Folders.Clear();
            AssetCatalog.Folders.AddRange(ReadFolders());
            AssetCatalog.ImportNewAssetsConfiguration.Imports.Clear();
            AssetCatalog.ImportNewAssetsConfiguration.Imports.AddRange(ReadImportDefinitions());
            AssetCatalog.Assets.ForEach(a => a.Folder = GetFolderById(a.FolderId));
            AssetCatalog.RecentTargetPaths.AddRange(ReadRecentTargetPaths());
        }

        public void SaveCatalog(Folder folder)
        {
            lock (AssetCatalog)
            {
                if (AssetCatalog.HasChanges)
                {
                    WriteAssets(AssetCatalog.Assets);
                    WriteFolders(AssetCatalog.Folders);
                    WriteImports(AssetCatalog.ImportNewAssetsConfiguration.Imports);
                    WriteRecentTargetPaths(AssetCatalog.RecentTargetPaths);
                }

                AssetCatalog.HasChanges = false;

                if (Thumbnails != null && folder != null && Thumbnails.ContainsKey(folder.Path))
                {
                    SaveThumbnails(Thumbnails[folder.Path], folder.ThumbnailsFilename);
                }

                if (database.WriteBackup(DateTime.Now.Date))
                {
                    database.DeleteOldBackups(userConfigurationService.GetBackupsToKeep());
                }
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
                        FolderId = f[0],
                        FileName = f[1],
                        FileSize = long.Parse(f[2]),
                        ImageRotation = (Rotation)Enum.Parse(typeof(Rotation), f[3]),
                        PixelWidth = int.Parse(f[4]),
                        PixelHeight = int.Parse(f[5]),
                        ThumbnailPixelWidth = int.Parse(f[6]),
                        ThumbnailPixelHeight = int.Parse(f[7]),
                        ThumbnailCreationDateTime = DateTime.Parse(f[8]),
                        Hash = f[9]
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

        public List<ImportNewAssetsDirectoriesDefinition> ReadImportDefinitions()
        {
            List<ImportNewAssetsDirectoriesDefinition> result;

            try
            {
                result = database.ReadObjectList("Import", f =>
                    new ImportNewAssetsDirectoriesDefinition
                    {
                        SourceDirectory = f[0],
                        DestinationDirectory = f[1],
                        IncludeSubFolders = bool.Parse(f[2])
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
                    0 => a.FolderId,
                    1 => a.FileName,
                    2 => a.FileSize,
                    3 => a.ImageRotation,
                    4 => a.PixelWidth,
                    5 => a.PixelHeight,
                    6 => a.ThumbnailPixelWidth,
                    7 => a.ThumbnailPixelHeight,
                    8 => a.ThumbnailCreationDateTime,
                    9 => a.Hash,
                    _ => throw new ArgumentOutOfRangeException(nameof(i))
                };
            });
        }

        public void WriteImports(List<ImportNewAssetsDirectoriesDefinition> imports)
        {
            database.WriteObjectList(imports, "Import", (d, i) =>
            {
                return i switch
                {
                    0 => d.SourceDirectory,
                    1 => d.DestinationDirectory,
                    2 => d.IncludeSubFolders,
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

        public bool FolderHasThumbnails(Folder folder)
        {
            string thumbnailsFilePath = database.ResolveBlobFilePath(dataDirectory, folder.ThumbnailsFilename);
            // TODO: Implement through the NuGet package.
            return File.Exists(thumbnailsFilePath);
        }

        private void DeleteThumbnails(Folder folder)
        {
            // TODO: Implement through the NuGet package.
            string thumbnailsFilePath = database.ResolveBlobFilePath(dataDirectory, folder.ThumbnailsFilename);
            File.Delete(thumbnailsFilePath);
        }

        protected virtual Dictionary<string, byte[]> GetThumbnails(string thumbnailsFileName, out bool isNewFile)
        {
            isNewFile = false;
            Dictionary<string, byte[]> thumbnails = (Dictionary<string, byte[]>)database.ReadBlob(thumbnailsFileName);

            if (thumbnails == null)
            {
                thumbnails = new Dictionary<string, byte[]>();
                isNewFile = true;
            }

            return thumbnails;
        }

        public bool HasChanges()
        {
            bool result = false;

            lock (AssetCatalog)
            {
                result = AssetCatalog.HasChanges;
            }

            return result;
        }

        private void SaveThumbnails(Dictionary<string, byte[]> thumbnails, string thumbnailsFileName)
        {
            database.WriteBlob(thumbnails, thumbnailsFileName);
        }

        public Asset[] GetAssets(string directory)
        {
            List<Asset> assetsList = null;

            try
            {
                lock (AssetCatalog)
                {
                    Folder folder = GetFolderByPath(directory);

                    if (folder != null)
                    {
                        assetsList = GetAssetsByFolderId(folder.FolderId);
                        var thumbnails = GetThumbnails(folder.ThumbnailsFilename, out bool isNewFile);

                        if (!isNewFile)
                        {
                            foreach (Asset asset in assetsList)
                            {
                                if (thumbnails.ContainsKey(asset.FileName))
                                {
                                    asset.ImageData = storageService.LoadBitmapImage(thumbnails[asset.FileName], asset.ThumbnailPixelWidth, asset.ThumbnailPixelHeight);
                                }
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

            return assetsList.ToArray();
        }

        public bool FolderExists(string path)
        {
            bool result = false;

            lock (AssetCatalog)
            {
                result = AssetCatalog.Folders.Any(f => f.Path == path);
            }

            return result;
        }

        public Folder AddFolder(string path)
        {
            Folder folder;

            lock (AssetCatalog)
            {
                string folderId = Guid.NewGuid().ToString();

                folder = new Folder
                {
                    FolderId = folderId,
                    Path = path
                };

                AssetCatalog.Folders.Add(folder);
                AssetCatalog.HasChanges = true;
            }

            return folder;
        }

        public void AddAsset(Asset asset, byte[] thumbnailData)
        {
            lock (AssetCatalog)
            {
                Folder folder = GetFolderById(asset.FolderId);

                if (folder == null)
                {
                    AddFolder(asset.Folder.Path);
                }

                if (!Thumbnails.ContainsKey(asset.Folder.Path))
                {
                    Thumbnails[asset.Folder.Path] = GetThumbnails(asset.Folder.ThumbnailsFilename, out bool isNewFile);
                }

                Thumbnails[asset.Folder.Path][asset.FileName] = thumbnailData;
                AssetCatalog.Assets.Add(asset);
                AssetCatalog.HasChanges = true;
            }
        }

        public Folder[] GetFolders()
        {
            Folder[] result;

            lock (AssetCatalog)
            {
                result = AssetCatalog.Folders.ToArray();
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

            lock (AssetCatalog)
            {
                result = AssetCatalog.Folders.FirstOrDefault(f => f.Path == path);
            }

            return result;
        }

        private Folder GetFolderById(string folderId)
        {
            Folder result = null;

            lock (AssetCatalog)
            {
                result = AssetCatalog.Folders.FirstOrDefault(f => f.FolderId == folderId);
            }

            return result;
        }

        private List<Asset> GetAssetsByFolderId(string folderId)
        {
            List<Asset> result = null;

            lock (AssetCatalog)
            {
                result = AssetCatalog.Assets.Where(a => a.FolderId == folderId).ToList();
            }

            return result;
        }

        private Asset GetAssetByFolderIdFileName(string folderId, string fileName)
        {
            Asset result = null;

            lock (AssetCatalog)
            {
                result = AssetCatalog.Assets.FirstOrDefault(a => a.FolderId == folderId && a.FileName == fileName);
            }

            return result;
        }

        public void DeleteAsset(string directory, string fileName)
        {
            lock (AssetCatalog)
            {
                Folder folder = GetFolderByPath(directory);

                if (folder != null)
                {
                    Asset deletedAsset = GetAssetByFolderIdFileName(folder.FolderId, fileName);

                    if (!Thumbnails.ContainsKey(folder.Path))
                    {
                        Thumbnails[folder.Path] = GetThumbnails(folder.ThumbnailsFilename, out bool isNewFile);
                    }

                    if (Thumbnails.ContainsKey(folder.Path))
                    {
                        Thumbnails[folder.Path].Remove(fileName);
                    }

                    if (deletedAsset != null)
                    {
                        AssetCatalog.Assets.Remove(deletedAsset);
                        AssetCatalog.HasChanges = true;
                    }
                }
            }
        }

        public void DeleteFolder(Folder folder)
        {
            lock (AssetCatalog)
            {
                if (folder != null)
                {
                    if (Thumbnails.ContainsKey(folder.Path))
                    {
                        Thumbnails.Remove(folder.Path);
                    }

                    if (FolderHasThumbnails(folder))
                    {
                        DeleteThumbnails(folder);
                    }

                    AssetCatalog.Folders.Remove(folder);
                    AssetCatalog.HasChanges = true;
                }
            }
        }

        public List<Asset> GetCataloguedAssets()
        {
            List<Asset> cataloguedAssets = null;

            lock (AssetCatalog)
            {
                cataloguedAssets = AssetCatalog.Assets;
            }

            return cataloguedAssets;
        }

        public List<Asset> GetCataloguedAssets(string directory)
        {
            List<Asset> cataloguedAssets = null;

            lock (AssetCatalog)
            {
                Folder folder = GetFolderByPath(directory);

                if (folder != null)
                {
                    cataloguedAssets = AssetCatalog.Assets.Where(a => a.FolderId == folder.FolderId).ToList();
                }
            }

            return cataloguedAssets;
        }

        public bool IsAssetCatalogued(string directoryName, string fileName)
        {
            bool result = false;

            lock (AssetCatalog)
            {
                Folder folder = GetFolderByPath(directoryName);
                result = folder != null && GetAssetByFolderIdFileName(folder.FolderId, fileName) != null;
            }

            return result;
        }

        public bool ContainsThumbnail(string directoryName, string fileName)
        {
            bool result = false;

            lock (AssetCatalog)
            {
                if (!Thumbnails.ContainsKey(directoryName))
                {
                    Folder folder = GetFolderByPath(directoryName);
                    Thumbnails[directoryName] = GetThumbnails(folder.ThumbnailsFilename, out bool isNewFile);
                }

                result = Thumbnails[directoryName].ContainsKey(fileName);
            }

            return result;
        }

        public BitmapImage LoadThumbnail(string directoryName, string fileName, int width, int height)
        {
            BitmapImage result = null;

            lock (AssetCatalog)
            {
                if (!Thumbnails.ContainsKey(directoryName))
                {
                    Folder folder = GetFolderByPath(directoryName);
                    Thumbnails[directoryName] = GetThumbnails(folder.ThumbnailsFilename, out bool isNewFile);
                }

                if (Thumbnails[directoryName].ContainsKey(fileName))
                {
                    result = storageService.LoadBitmapImage(Thumbnails[directoryName][fileName], width, height);
                }
                else
                {
                    DeleteAsset(directoryName, fileName);
                    Folder folder = GetFolderByPath(directoryName);
                    SaveCatalog(folder);
                }
            }

            return result;
        }

        public ImportNewAssetsConfiguration GetImportNewAssetsConfiguration()
        {
            ImportNewAssetsConfiguration result;

            lock (AssetCatalog)
            {
                result = AssetCatalog.ImportNewAssetsConfiguration;
            }

            return result;
        }

        public void SetImportNewAssetsConfiguration(ImportNewAssetsConfiguration importConfiguration)
        {
            lock (AssetCatalog)
            {
                AssetCatalog.ImportNewAssetsConfiguration = importConfiguration;
                AssetCatalog.HasChanges = true;
            }
        }

        public List<string> GetRecentTargetPaths()
        {
            List<string> result = null;

            lock (AssetCatalog)
            {
                result = AssetCatalog.RecentTargetPaths;
            }

            return result;
        }

        public void SetRecentTargetPaths(List<string> recentTargetPaths)
        {
            lock (AssetCatalog)
            {
                AssetCatalog.RecentTargetPaths = recentTargetPaths;
                AssetCatalog.HasChanges = true;
            }
        }
    }
}
