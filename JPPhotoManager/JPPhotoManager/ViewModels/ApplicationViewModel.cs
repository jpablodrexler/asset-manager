﻿using JPPhotoManager.Application;
using JPPhotoManager.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JPPhotoManager.ViewModels
{
    public class ApplicationViewModel : BaseViewModel<IJPPhotoManagerApplication>
    {
        private AppModeEnum appMode;
        private int viewerPosition;
        private string currentFolder;
        private ObservableCollection<Asset> files;
        private ImageSource currentImageSource;
        private string appTitle;
        private string statusMessage;

        public string Product { get; set; }
        public string Version { get; set; }

        public ApplicationViewModel(IJPPhotoManagerApplication assetApp) : base(assetApp)
        {
            var folder = this.Application.GetInitialFolder();
            this.CurrentFolder = folder;
        }

        public AppModeEnum AppMode
        {
            get { return this.appMode; }
            private set
            {
                this.appMode = value;
                this.NotifyPropertyChanged(nameof(AppMode), nameof(ThumbnailsVisible), nameof(ViewerVisible));
                this.UpdateAppTitle();
            }
        }

        public void ChangeAppMode()
        {
            if (this.AppMode == AppModeEnum.Viewer)
            {
                this.AppMode = AppModeEnum.Thumbnails;
            }
            else if (this.AppMode == AppModeEnum.Thumbnails)
            {
                this.AppMode = AppModeEnum.Viewer;
            }
        }

        public void ChangeAppMode(AppModeEnum appMode)
        {
            this.AppMode = appMode;
        }

        public Visibility ThumbnailsVisible
        {
            get { return this.AppMode == AppModeEnum.Thumbnails ? Visibility.Visible : Visibility.Hidden; }
        }

        public Visibility ViewerVisible
        {
            get { return this.AppMode == AppModeEnum.Viewer ? Visibility.Visible : Visibility.Hidden; }
        }

        public int ViewerPosition
        {
            get { return this.viewerPosition; }
            set
            {
                this.viewerPosition = value;
                this.NotifyPropertyChanged(nameof(ViewerPosition), nameof(CurrentAsset));
                this.UpdateAppTitle();
            }
        }

        public string CurrentFolder
        {
            get { return this.currentFolder; }
            set
            {
                this.currentFolder = value;
                this.NotifyPropertyChanged(nameof(CurrentFolder));
                this.UpdateAppTitle();
            }
        }

        public ObservableCollection<Asset> Files
        {
            get { return this.files; }
            set
            {
                this.files = value;
                this.NotifyPropertyChanged(nameof(Files));
                this.UpdateAppTitle();
            }
        }

        public string AppTitle
        {
            get { return this.appTitle; }
            set
            {
                this.appTitle = value;
                this.NotifyPropertyChanged(nameof(AppTitle));
            }
        }

        public string StatusMessage
        {
            get { return this.statusMessage; }
            set
            {
                this.statusMessage = value;
                this.NotifyPropertyChanged(nameof(StatusMessage));
            }
        }

        public Asset CurrentAsset
        {
            get { return this.Files?.Count > 0 && this.ViewerPosition >= 0 ? this.Files?[this.ViewerPosition] : null; }
        }

        private void AddAsset(Asset asset)
        {
            if (this.Files != null)
            {
                this.Files.Add(asset);
                this.NotifyPropertyChanged(nameof(Files));
            }
        }

        public void RemoveAsset(Asset asset)
        {
            if (this.Files != null)
            {
                this.Files.Remove(asset);

                if ((this.ViewerPosition + 1) < this.Files.Count)
                {
                    this.ViewerPosition = (this.ViewerPosition + 1);
                }

                this.NotifyPropertyChanged(nameof(Files));
            }
        }

        private void UpdateAppTitle()
        {
            string title = null;

            if (this.AppMode == AppModeEnum.Thumbnails)
            {
                title = string.Format("{0} {1} - {2}", this.Product, this.Version, this.CurrentFolder);
            }
            else if (this.AppMode == AppModeEnum.Viewer)
            {
                title = string.Format("{0} {1} - {2} - imagen {3} de {4}", this.Product, this.Version, this.CurrentAsset?.FileName, this.ViewerPosition + 1, this.Files?.Count);
            }

            this.AppTitle = title;
        }

        public void GoToImage(Asset asset)
        {
            this.GoToImage(asset, this.AppMode);
        }

        public void GoToImage(Asset asset, AppModeEnum newAppMode)
        {
            Asset targetAsset = this.Files.FirstOrDefault(f => f.FileName == asset.FileName);

            if (targetAsset != null && File.Exists(targetAsset.FullPath))
            {
                int position = this.Files.IndexOf(targetAsset);
                this.ChangeAppMode(newAppMode);
                this.ViewerPosition = position;
            }
        }

        public void GoToPreviousImage()
        {
            if (this.ViewerPosition > 0)
            {
                this.ViewerPosition--;
            }
        }

        public void GoToNextImage()
        {
            if (this.ViewerPosition < (this.Files.Count - 1))
            {
                this.ViewerPosition++;
            }
        }

        public void NotifyCatalogChange(CatalogChangeCallbackEventArgs e)
        {
            this.StatusMessage = e.Message;

            if (e?.Asset?.Folder?.Path == this.CurrentFolder)
            {
                switch (e.Reason)
                {
                    case ReasonEnum.Created:
                        // If the files list is empty or belongs to other directory
                        if ((this.Files.Count == 0 || this.Files[0].Folder.Path != this.CurrentFolder) && e.CataloguedAssets != null)
                        {
                            this.Files = new ObservableCollection<Asset>(e.CataloguedAssets.Where(a => a.ImageData != null).ToList());
                        }
                        else
                        {
                            this.AddAsset(e.Asset);
                        }
                        
                        break;

                    case ReasonEnum.Updated:
                        // TODO: IMPLEMENT.
                        break;

                    case ReasonEnum.Deleted:
                        this.RemoveAsset(e.Asset);
                        break;
                }
            }
        }
    }
}
