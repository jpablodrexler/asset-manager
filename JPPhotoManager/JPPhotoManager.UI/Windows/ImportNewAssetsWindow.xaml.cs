﻿using JPPhotoManager.Domain;
using JPPhotoManager.Infrastructure;
using JPPhotoManager.UI.ViewModels;
using log4net;
using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JPPhotoManager.UI.Windows
{
    /// <summary>
    /// Interaction logic for ImportNewAssetsWindow.xaml
    /// </summary>
    [ExcludeFromCodeCoverage]
    public partial class ImportNewAssetsWindow : Window
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public ImportNewAssetsWindow(ImportNewAssetsViewModel viewModel)
        {
            try
            {
                InitializeComponent();

                DataContext = viewModel;
                Initialize();
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        public ImportNewAssetsViewModel ViewModel
        {
            get { return (ImportNewAssetsViewModel)DataContext; }
        }

        private void Initialize()
        {
            var configuration = ViewModel.GetProcessConfiguration();

            if (configuration == null)
            {
                configuration = new ImportNewAssetsConfiguration();
            }

            ViewModel.Imports = new ObservableCollection<ImportNewAssetsDirectoriesDefinition>(configuration.Imports);
        }

        private void DeleteLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DeleteDefinition(((TextBlock)e.Source).DataContext);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private void MoveUpLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                MoveUpDefinition(((TextBlock)e.Source).DataContext);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private void MoveDownLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                MoveDownDefinition(((TextBlock)e.Source).DataContext);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private void SaveButton_MouseLeftButtonDown(object sender, RoutedEventArgs e)
        {
            try
            {
                Cursor = Cursors.Wait;
                Save(ViewModel.Imports);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private async void ImportButton_MouseLeftButtonDown(object sender, RoutedEventArgs e)
        {
            try
            {
                Cursor = Cursors.Wait;
                ViewModel.AdvanceStep();
                await RunProcess().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private void ViewResultsButton_MouseLeftButtonDown(object sender, RoutedEventArgs e)
        {
            try
            {
                Cursor = Cursors.Wait;
                ViewModel.AdvanceStep();
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private void DeleteDefinition(object selected)
        {
            // Evaluates if it is an existing item or the NewItemPlaceholder.
            if (selected is ImportNewAssetsDirectoriesDefinition definition)
            {
                ViewModel.DeleteDefinition(definition);
            }
        }

        private void MoveUpDefinition(object selected)
        {
            // Evaluates if it is an existing item or the NewItemPlaceholder.
            if (selected is ImportNewAssetsDirectoriesDefinition definition)
            {
                ViewModel.MoveUpDefinition(definition);
            }
        }

        private void MoveDownDefinition(object selected)
        {
            // Evaluates if it is an existing item or the NewItemPlaceholder.
            if (selected is ImportNewAssetsDirectoriesDefinition definition)
            {
                ViewModel.MoveDownDefinition(definition);
            }
        }

        private void Save(ObservableCollection<ImportNewAssetsDirectoriesDefinition> imports)
        {
            ImportNewAssetsConfiguration configuration = new();
            configuration.Imports.AddRange(imports);
            ViewModel.SetProcessConfiguration(configuration);
        }

        private async Task RunProcess()
        {
            Save(ViewModel.Imports);
            await ViewModel.RunProcessAsync(e => Dispatcher.Invoke(() => ViewModel.NotifyProcessStatusChanged(e)));
        }
    }
}
