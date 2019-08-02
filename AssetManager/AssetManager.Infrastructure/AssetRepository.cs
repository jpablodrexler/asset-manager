﻿using AssetManager.Domain;
using log4net;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace AssetManager.Infrastructure
{
    public class AssetRepository : IAssetRepository
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public bool IsInitialized { get; private set; }
        private string dataDirectory;
        private string assetCatalogPath;
        private IStorageService storageService;
        private IUserConfigurationService userConfigurationService;

        protected AssetCatalog AssetCatalog { get; private set; }

        public AssetRepository(IStorageService storageService, IUserConfigurationService userConfigurationService)
        {
            this.storageService = storageService;
            this.userConfigurationService = userConfigurationService;
        }

        public void Initialize(string dataDirectory = "")
        {
            if (!this.IsInitialized)
            {
                this.dataDirectory = this.storageService.ResolveDataDirectory(dataDirectory);
                this.assetCatalogPath = this.storageService.ResolveCatalogPath(this.dataDirectory);
                this.ReadCatalog();

                if (this.AssetCatalog == null)
                {
                    this.AssetCatalog = new AssetCatalog();
                    this.storageService.CreateDirectory(this.dataDirectory);
                    this.AssetCatalog.StorageVersion = 2.0;
                    SaveCatalog(null, null);
                }

                this.IsInitialized = true;
            }
        }

        private void ReadCatalog()
        {
            this.AssetCatalog = this.storageService.ReadObjectFromJson<AssetCatalog>(this.assetCatalogPath);

            if (this.AssetCatalog != null)
            {
                this.AssetCatalog.Assets.ForEach(a => a.Folder = GetFolderById(a.FolderId));
            }
        }

        public void SaveCatalog(Dictionary<string, byte[]> thumbnails, string thumbnailsFileName)
        {
            lock (this.AssetCatalog)
            {
                this.storageService.WriteObjectToJson(this.AssetCatalog, this.assetCatalogPath);
                this.AssetCatalog.HasChanges = false;
            }

            if (thumbnails != null && !string.IsNullOrEmpty(thumbnailsFileName))
            {
                this.SaveThumbnails(thumbnails, thumbnailsFileName);
            }
        }

        public virtual Dictionary<string, byte[]> GetThumbnails(string thumbnailsFileName, out bool isNewFile)
        {
            isNewFile = false;
            string thumbnailsFilePath = this.storageService.ResolveThumbnailsFilePath(dataDirectory, thumbnailsFileName);
            Dictionary<string, byte[]> thumbnails = (Dictionary<string, byte[]>)this.storageService.ReadObjectFromBinaryFile(thumbnailsFilePath);

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
            string thumbnailsFilePath = this.storageService.ResolveThumbnailsFilePath(dataDirectory, thumbnailsFileName);
            this.storageService.WriteObjectToBinaryFile(thumbnails, thumbnailsFilePath);
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
                            foreach (Asset assetRow in assetsList)
                            {
                                assetRow.ImageData = this.storageService.LoadBitmapImage(thumbnails[assetRow.FileName]);
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

        public void AddAsset(Asset asset)
        {
            lock (this.AssetCatalog)
            {
                Folder folder = GetFolderById(asset.FolderId);

                if (folder == null)
                {
                    this.AddFolder(folder.Path);
                }

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

        public void DeleteAsset(string directory, string fileName, bool deleteFile)
        {
            if (deleteFile)
            {
                this.storageService.DeleteFile(directory, fileName);
            }

            lock (this.AssetCatalog)
            {
                Folder folder = GetFolderByPath(directory);

                if (folder != null)
                {
                    Asset deletedAsset = GetAssetByFolderIdFileName(folder.FolderId, fileName);

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
    }
}
