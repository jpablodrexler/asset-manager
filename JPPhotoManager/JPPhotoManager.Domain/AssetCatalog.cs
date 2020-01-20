﻿using System.Collections.Generic;

namespace JPPhotoManager.Domain
{
    public class AssetCatalog
    {
        public bool HasChanges { get; set; }
        public double StorageVersion { get; set; }
        public List<Folder> Folders { get; }
        public List<Asset> Assets { get; }
        public ImportNewAssetsConfiguration ImportNewAssetsConfiguration { get; set; }

        public AssetCatalog()
        {
            this.Folders = new List<Folder>();
            this.Assets = new List<Asset>();
            this.ImportNewAssetsConfiguration = new ImportNewAssetsConfiguration();
        }
    }
}
