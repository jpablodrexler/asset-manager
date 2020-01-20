using JPPhotoManager.Domain;
using log4net;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Media.Imaging;

namespace JPPhotoManager.Infrastructure
{
    public class AssetRepository : IAssetRepository
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public bool IsInitialized { get; private set; }
        private string dataDirectory;
        private string assetsDataFilePath;
        private string foldersDataFilePath;
        private string importsDataFilePath;
        private IStorageService storageService;
        
        protected AssetCatalog AssetCatalog { get; private set; }
        private Dictionary<string, Dictionary<string, byte[]>> thumbnails;

        public AssetRepository(IStorageService storageService)
        {
            this.storageService = storageService;
            this.thumbnails = new Dictionary<string, Dictionary<string, byte[]>>();
        }

        public void Initialize(string assetsDataFilePath = null, string foldersDataFilePath = null, string importsDataFilePath = null)
        {
            if (!this.IsInitialized)
            {
                this.dataDirectory = this.storageService.ResolveDataDirectory();
                this.assetsDataFilePath = string.IsNullOrEmpty(assetsDataFilePath) ? this.storageService.ResolveTableFilePath(this.dataDirectory, "asset") : assetsDataFilePath;
                this.foldersDataFilePath = string.IsNullOrEmpty(foldersDataFilePath) ? this.storageService.ResolveTableFilePath(this.dataDirectory, "folder") : foldersDataFilePath;
                this.importsDataFilePath = string.IsNullOrEmpty(importsDataFilePath) ? this.storageService.ResolveTableFilePath(this.dataDirectory, "import") : importsDataFilePath;
                this.ReadCatalog();

                if (this.AssetCatalog == null)
                {
                    this.AssetCatalog = new AssetCatalog();
                    this.storageService.CreateDirectory(this.dataDirectory);
                    this.storageService.CreateDirectory(this.storageService.GetTablesDirectory(this.dataDirectory));
                    this.storageService.CreateDirectory(this.storageService.GetBlobsDirectory(this.dataDirectory));
                    this.AssetCatalog.StorageVersion = 2.0;
                    SaveCatalog(null);
                }

                this.IsInitialized = true;
            }
        }

        private void ReadCatalog()
        {
            if (this.storageService.FileExists(this.assetsDataFilePath)
                && this.storageService.FileExists(this.foldersDataFilePath)
                && this.storageService.FileExists(this.importsDataFilePath))
            {
                this.AssetCatalog = new AssetCatalog();
                this.AssetCatalog.Assets.Clear();
                this.AssetCatalog.Assets.AddRange(ReadAssetsFromCsv());
                this.AssetCatalog.Folders.Clear();
                this.AssetCatalog.Folders.AddRange(ReadFoldersFromCsv());
                this.AssetCatalog.ImportNewAssetsConfiguration.Imports.Clear();
                this.AssetCatalog.ImportNewAssetsConfiguration.Imports.AddRange(ReadImportDefinitionsFromCsv());
                this.AssetCatalog.Assets.ForEach(a => a.Folder = GetFolderById(a.FolderId));
            }
        }

        public void SaveCatalog(Folder folder)
        {
            lock (this.AssetCatalog)
            {
                if (this.AssetCatalog.HasChanges)
                {
                    WriteAssetsToCsvFile(this.AssetCatalog.Assets);
                    WriteFoldersToCsvFile(this.AssetCatalog.Folders);
                    WriteImportsToCsvFile(this.AssetCatalog.ImportNewAssetsConfiguration.Imports);
                }
                
                this.AssetCatalog.HasChanges = false;

                if (thumbnails != null && folder != null && thumbnails.ContainsKey(folder.Path))
                {
                    this.SaveThumbnails(thumbnails[folder.Path], folder.ThumbnailsFilename);
                }
            }
        }

        public List<Folder> ReadFoldersFromCsv()
        {
            List<Folder> result = this.storageService.ReadFromCsv(
                this.foldersDataFilePath,
                r => new Folder
                {
                    FolderId = r[0],
                    Path = r[1]
                });
            
            return result;
        }

        public List<Asset> ReadAssetsFromCsv()
        {
            List<Asset> result = this.storageService.ReadFromCsv(
                this.assetsDataFilePath,
                r => new Asset
                {
                    FolderId = r[0],
                    FileName = r[1],
                    FileSize = long.Parse(r[2]),
                    ImageRotation = (Rotation)Enum.Parse(typeof(Rotation), r[3]),
                    PixelWidth = int.Parse(r[4]),
                    PixelHeight = int.Parse(r[5]),
                    ThumbnailPixelWidth = int.Parse(r[6]),
                    ThumbnailPixelHeight = int.Parse(r[7]),
                    ThumbnailCreationDateTime = DateTime.Parse(r[8]),
                    Hash = r[9]
                });

            return result;
        }

        public List<ImportNewAssetsDirectoriesDefinition> ReadImportDefinitionsFromCsv()
        {
            List<ImportNewAssetsDirectoriesDefinition> result = this.storageService.ReadFromCsv(
                this.importsDataFilePath,
                r => new ImportNewAssetsDirectoriesDefinition
                {
                    SourceDirectory = r[0],
                    DestinationDirectory = r[1],
                    IncludeSubFolders = bool.Parse(r[2])
                });

            return result;
        }

        public void WriteFoldersToCsvFile(List<Folder> folders)
        {
            this.storageService.WriteToCsvFile(
                this.foldersDataFilePath,
                folders,
                new string[]
                {
                    nameof(Folder.FolderId),
                    nameof(Folder.Path)
                },
                f => new object[]
                {
                    f.FolderId,
                    f.Path
                });
        }

        public void WriteAssetsToCsvFile(List<Asset> assets)
        {
            this.storageService.WriteToCsvFile(
                this.assetsDataFilePath,
                assets,
                new string[]
                {
                    nameof(Asset.FolderId),
                    nameof(Asset.FileName),
                    nameof(Asset.FileSize),
                    nameof(Asset.ImageRotation),
                    nameof(Asset.PixelWidth),
                    nameof(Asset.PixelHeight),
                    nameof(Asset.ThumbnailPixelWidth),
                    nameof(Asset.ThumbnailPixelHeight),
                    nameof(Asset.ThumbnailCreationDateTime),
                    nameof(Asset.Hash)
                },
                a => new object[]
                {
                    a.FolderId,
                    a.FileName,
                    a.FileSize,
                    a.ImageRotation,
                    a.PixelWidth,
                    a.PixelHeight,
                    a.ThumbnailPixelWidth,
                    a.ThumbnailPixelHeight,
                    a.ThumbnailCreationDateTime,
                    a.Hash
                });
        }

        public void WriteImportsToCsvFile(List<ImportNewAssetsDirectoriesDefinition> imports)
        {
            this.storageService.WriteToCsvFile(
                this.importsDataFilePath,
                imports,
                new string[]
                {
                    nameof(ImportNewAssetsDirectoriesDefinition.SourceDirectory),
                    nameof(ImportNewAssetsDirectoriesDefinition.DestinationDirectory),
                    nameof(ImportNewAssetsDirectoriesDefinition.IncludeSubFolders)
                },
                i => new object[]
                {
                    i.SourceDirectory,
                    i.DestinationDirectory,
                    i.IncludeSubFolders
                });
        }

        public bool FolderHasThumbnails(Folder folder)
        {
            string thumbnailsFilePath = this.storageService.ResolveBlobFilePath(dataDirectory, folder.ThumbnailsFilename);
            return File.Exists(thumbnailsFilePath);
        }

        protected virtual Dictionary<string, byte[]> GetThumbnails(string thumbnailsFileName, out bool isNewFile)
        {
            isNewFile = false;
            string thumbnailsFilePath = this.storageService.ResolveBlobFilePath(dataDirectory, thumbnailsFileName);
            Dictionary<string, byte[]> thumbnails = (Dictionary<string, byte[]>)this.storageService.ReadFromBinaryFile(thumbnailsFilePath);

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

            lock (this.AssetCatalog)
            {
                result = this.AssetCatalog.HasChanges;
            }

            return result;
        }

        private void SaveThumbnails(Dictionary<string, byte[]> thumbnails, string thumbnailsFileName)
        {
            string thumbnailsFilePath = this.storageService.ResolveBlobFilePath(dataDirectory, thumbnailsFileName);
            this.storageService.WriteToBinaryFile(thumbnails, thumbnailsFilePath);
        }

        public Asset[] GetAssets(string directory)
        {
            List<Asset> assetsList = null;

            try
            {
                lock (this.AssetCatalog)
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
                                    asset.ImageData = this.storageService.LoadBitmapImage(thumbnails[asset.FileName], asset.ThumbnailPixelWidth, asset.ThumbnailPixelHeight);
                                }
                            }
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

            lock (this.AssetCatalog)
            {
                result = this.AssetCatalog.Folders.Any(f => f.Path == path);
            }

            return result;
        }

        public Folder AddFolder(string path)
        {
            Folder folder;

            lock (this.AssetCatalog)
            {
                string folderId = Guid.NewGuid().ToString();

                folder = new Folder
                {
                    FolderId = folderId,
                    Path = path
                };

                this.AssetCatalog.Folders.Add(folder);
                this.AssetCatalog.HasChanges = true;
            }

            return folder;
        }

        public void AddAsset(Asset asset, byte[] thumbnailData)
        {
            lock (this.AssetCatalog)
            {
                Folder folder = GetFolderById(asset.FolderId);

                if (folder == null)
                {
                    this.AddFolder(asset.Folder.Path);
                }

                if (!this.thumbnails.ContainsKey(asset.Folder.Path))
                {
                    this.thumbnails[asset.Folder.Path] = this.GetThumbnails(asset.Folder.ThumbnailsFilename, out bool isNewFile);
                }

                this.thumbnails[asset.Folder.Path][asset.FileName] = thumbnailData;
                this.AssetCatalog.Assets.Add(asset);
                this.AssetCatalog.HasChanges = true;
            }
        }

        public Folder[] GetFolders()
        {
            Folder[] result;

            lock (this.AssetCatalog)
            {
                result = this.AssetCatalog.Folders.ToArray();
            }

            return result;
        }

        public Folder GetFolderByPath(string path)
        {
            Folder result = null;

            lock (this.AssetCatalog)
            {
                result = this.AssetCatalog.Folders.FirstOrDefault(f => f.Path == path);
            }

            return result;
        }

        private Folder GetFolderById(string folderId)
        {
            Folder result = null;

            lock (this.AssetCatalog)
            {
                result = this.AssetCatalog.Folders.FirstOrDefault(f => f.FolderId == folderId);
            }

            return result;
        }

        private List<Asset> GetAssetsByFolderId(string folderId)
        {
            List<Asset> result = null;

            lock (this.AssetCatalog)
            {
                result = this.AssetCatalog.Assets.Where(a => a.FolderId == folderId).ToList();
            }

            return result;
        }

        private Asset GetAssetByFolderIdFileName(string folderId, string fileName)
        {
            Asset result = null;

            lock (this.AssetCatalog)
            {
                result = this.AssetCatalog.Assets.FirstOrDefault(a => a.FolderId == folderId && a.FileName == fileName);
            }

            return result;
        }

        public void DeleteAsset(string directory, string fileName)
        {
            lock (this.AssetCatalog)
            {
                Folder folder = GetFolderByPath(directory);

                if (folder != null)
                {
                    Asset deletedAsset = GetAssetByFolderIdFileName(folder.FolderId, fileName);

                    if (this.thumbnails.ContainsKey(folder.Path))
                    {
                        this.thumbnails[folder.Path].Remove(fileName);
                    }

                    if (deletedAsset != null)
                    {
                        this.AssetCatalog.Assets.Remove(deletedAsset);
                    }
                }
            }
        }

        public List<Asset> GetCataloguedAssets()
        {
            List<Asset> cataloguedAssets = null;

            lock (this.AssetCatalog)
            {
                cataloguedAssets = this.AssetCatalog.Assets;
            }

            return cataloguedAssets;
        }

        public List<Asset> GetCataloguedAssets(string directory)
        {
            List<Asset> cataloguedAssets = null;

            lock (this.AssetCatalog)
            {
                Folder folder = GetFolderByPath(directory);

                if (folder != null)
                {
                    cataloguedAssets = this.AssetCatalog.Assets.Where(a => a.FolderId == folder.FolderId).ToList();
                }
            }

            return cataloguedAssets;
        }

        public bool IsAssetCatalogued(string directoryName, string fileName)
        {
            bool result = false;

            lock (this.AssetCatalog)
            {
                Folder folder = GetFolderByPath(directoryName);
                result = folder != null && GetAssetByFolderIdFileName(folder.FolderId, fileName) != null;
            }

            return result;
        }

        public bool ContainsThumbnail(string directoryName, string fileName)
        {
            bool result = false;

            lock (this.AssetCatalog)
            {
                if (!this.thumbnails.ContainsKey(directoryName))
                {
                    Folder folder = GetFolderByPath(directoryName);
                    this.thumbnails[directoryName] = GetThumbnails(folder.ThumbnailsFilename, out bool isNewFile);
                }

                result = this.thumbnails[directoryName].ContainsKey(fileName);
            }

            return result;
        }

        public BitmapImage LoadThumbnail(string directoryName, string fileName, int width, int height)
        {
            BitmapImage result;

            lock (this.AssetCatalog)
            {
                if (!this.thumbnails.ContainsKey(directoryName))
                {
                    Folder folder = GetFolderByPath(directoryName);
                    this.thumbnails[directoryName] = GetThumbnails(folder.ThumbnailsFilename, out bool isNewFile);
                }

                result = this.storageService.LoadBitmapImage(thumbnails[directoryName][fileName], width, height);
            }

            return result;
        }

        public ImportNewAssetsConfiguration GetImportNewAssetsConfiguration()
        {
            ImportNewAssetsConfiguration result;

            lock (this.AssetCatalog)
            {
                result = this.AssetCatalog.ImportNewAssetsConfiguration;
            }

            return result;
        }

        public void SetImportNewAssetsConfiguration(ImportNewAssetsConfiguration importConfiguration)
        {
            lock (this.AssetCatalog)
            {
                this.AssetCatalog.ImportNewAssetsConfiguration = importConfiguration;
                this.AssetCatalog.HasChanges = true;
            }
        }
    }
}
