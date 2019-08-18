﻿using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;

namespace JPPhotoManager.Domain
{
    public class Asset
    {
        public string FolderId { get; set; }

        [JsonIgnore]
        public Folder Folder { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public DateTime ThumbnailCreationDateTime { get; set; }
        public string Hash { get; set; }

        [JsonIgnore]
        public BitmapImage ImageData { get; set; }

        [JsonIgnore]
        public string FullPath => Path.Combine(this.Folder.Path, this.FileName);

        [JsonIgnore]
        public string FormattedPixelSize
        {
            get
            {
                return $"{this.PixelWidth}x{this.PixelHeight}";
            }
        }

        public override bool Equals(object obj)
        {
            
            Asset asset = obj as Asset;

            return asset != null && asset.FolderId == this.FolderId && asset.FileName == this.FileName;
        }

        public override int GetHashCode()
        {
            return (!string.IsNullOrEmpty(this.FolderId) ? this.FolderId.GetHashCode() : base.GetHashCode()) + (!string.IsNullOrEmpty(this.FileName) ? this.FileName.GetHashCode() : base.GetHashCode());
        }

        public override string ToString()
        {
            return this.FileName;
        }
    }
}
