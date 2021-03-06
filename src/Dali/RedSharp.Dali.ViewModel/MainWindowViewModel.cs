﻿using ReactiveUI;
using RedSharp.Dali.Common.Interfaces.Services;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData.Binding;
using RedSharp.Dali.Common.Enums;
using System.Reactive.Linq;
using DynamicData;
using RedSharp.Dali.Common.Events;
using System.IO;
using RedSharp.Dali.Common.Data;
using RedSharp.Dali.Common.Interfaces;
using RedSharp.Dali.Common.Interfaces.ViewModels;
using System.Windows.Input;

namespace RedSharp.Dali.ViewModel
{
    /// <summary>
    /// View model for main application window.
    /// </summary>
    public class MainWindowViewModel : ReactiveObject, IHotkeyProcessor, IMainWindowViewModel
    {
        /// <summary>
        /// List of supproted formats to check extension manually in case of drag and drop.
        /// Should be in configs too.
        /// </summary>
        /// <remarks>
        /// We should introduce IImageManager to work with images.
        /// </remarks>
        private static readonly IReadOnlyCollection<string> SupportedFormats = new ReadOnlyCollection<string>(new[]
        {
            ".bmp",
            ".jpg",
            ".jpeg",
            ".png",
            ".gif"
        });

        #region Fields
        private readonly IDialogService _dialogService;
        private readonly IDispatcher _dispatcher;

        private TransparentWindowViewModel _transparentWindowViewModel;

        //Items to save open images.
        //Actual storage. Please work with it.
        private SourceList<ImageItem> _images = new SourceList<ImageItem>();

        //Buffer for public access. Here might be transformed or filtered items if 
        //such operation will be applied.
        private ReadOnlyObservableCollection<IImageItem> _readOnlyBuff;

        //Subscription holder.
        private readonly IDisposable _imagesSubscription;

        private ReactiveCommand<Unit, Unit> _startCommand;
        private ReactiveCommand<Unit, Unit> _loadCommand;
        private ReactiveCommand<Unit, Unit> _saveCommand;
        private ReactiveCommand<Unit, Unit> _removeCommand;

        private ReactiveCommand<DragAndDropEventArgs, Unit> _dragEnterCommand;
        private ReactiveCommand<DragAndDropEventArgs, Unit> _dragOverCommand;
        private ReactiveCommand<DragAndDropEventArgs, Unit> _dropCommand;

        #endregion

        #region Construction
        public MainWindowViewModel(ISettingsProvider settingsProvider,
                                   IDialogService dialogService,
                                   IDispatcher dispatcher)
        {
            _dialogService = dialogService;
            _dispatcher = dispatcher;

            Settings = settingsProvider;

            _imagesSubscription = _images.Cast(i => i as IImageItem).Bind(out _readOnlyBuff).Subscribe();
        }

        #endregion

        #region Commands

        /// <summary>
        /// Actual implementation of <see cref="IMainWindowViewModel.StartCommand"/>.
        /// </summary>
        public ReactiveCommand<Unit, Unit> StartCommand
        {
            get
            {
                return _startCommand ?? (_startCommand = ReactiveCommand.Create(() =>
               {
                   if (_transparentWindowViewModel == null)
                        _transparentWindowViewModel = new TransparentWindowViewModel(Images.First(im => im.IsSelected));

                   _dialogService.ShowWindow(DaliWindowsEnum.WorkAreaWindow, _transparentWindowViewModel);

               }, _images.Connect().WhenPropertyChanged(item => item.IsSelected).Select(res => _images.Items.Any(item => item.IsSelected))));
            }
        }

        /// <summary>
        /// Actual implementation of <see cref="IMainWindowViewModel.LoadCommand"/>.
        /// </summary>
        public ReactiveCommand<Unit, Unit> LoadCommand
        {
            get
            {
                return _loadCommand ?? (_loadCommand = ReactiveCommand.Create(() =>
                {
                    string formatsString = SupportedFormats.Select(format => $"*{format};").Aggregate((accum, val) => accum += val);

                    IEnumerable<string> files = _dialogService.ShowOpenFileDialog(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), new Dictionary<string, string>() { { formatsString, "Images"} });
                    if (files.Any())
                        OpenFiles(files);
                }));
            }
        }

        /// <summary>
        /// Actual implamentation of <see cref="IMainWindowViewModel.SaveCommand"/>.
        /// </summary>
        public ReactiveCommand<Unit, Unit> SaveCommand
        {
            get
            {
                return _saveCommand ?? (_saveCommand = ReactiveCommand.Create(() =>
                {
                    foreach (ImageItem item in _images.Items.Where(item => item.IsSelected))
                    {
                        string path = _dialogService.ShowSaveFileDialog(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), SupportedFormats.ToDictionary(val => $"*{val}", val => val.Trim('.').ToUpper()));

                        if (string.IsNullOrEmpty(path))
                            continue;

                        item.SaveImage(path);
                    }
                }, _images.Connect().WhenPropertyChanged(item => item.IsSelected).Select(res => _images.Items.Count(im => im.IsSelected) == 1)));
            }
        }

        /// <summary>
        /// Actual implementation of <see cref="IMainWindowViewModel.RemoveCommand"/>.
        /// </summary>
        public ReactiveCommand<Unit, Unit> RemoveCommand
        {
            get
            {
                return _removeCommand ?? (_removeCommand = ReactiveCommand.Create(() =>
                {
                    IEnumerable<IImageItem> selected = Images.Where(image => image.IsSelected).ToArray();
                    foreach (ImageItem item in selected)
                    {
                        item.IsSelected = false;
                        _images.Remove(item);
                    }
                }, _images.Connect().WhenPropertyChanged(item=>item.IsSelected).Select(res => _images.Items.Any(item => item.IsSelected))));
            }
        }

        /// <summary>
        /// Actual implementation of <see cref="IMainWindowViewModel.DragEnterCommand"/>.
        /// </summary>
        public ReactiveCommand<DragAndDropEventArgs, Unit> DragEnterCommand
        {
            get
            {
                return _dragEnterCommand ?? (_dragEnterCommand = ReactiveCommand.Create<DragAndDropEventArgs>(args =>
                {
                    CheckDropData(args);
                }));
            }
        }

        /// <summary>
        /// Actual implementation of <see cref="IMainWindowViewModel.DragOverCommand"/>.
        /// </summary>
        public ReactiveCommand<DragAndDropEventArgs, Unit> DragOverCommand
        {
            get
            {
                return _dragOverCommand ?? (_dragOverCommand = ReactiveCommand.Create<DragAndDropEventArgs>(args =>
                {
                    CheckDropData(args);
                }));
            }
        }

        /// <summary>
        /// Actual implementation of <see cref="IMainWindowViewModel.DropCommand"/>.
        /// </summary>
        public ReactiveCommand<DragAndDropEventArgs, Unit> DropCommand
        {
            get
            {
                return _dropCommand ?? (_dropCommand = ReactiveCommand.Create<DragAndDropEventArgs>(args =>
                {
                    if (args.Effects != DragAndDropEffectsEnum.None)
                    {
                        if(args.Data.ContainsKey(DropTypeEnum.FilePath))
                        {
                            OpenFiles(args.Data[DropTypeEnum.FilePath] as IEnumerable<string>);
                        }
                        else if (args.Data.ContainsKey(DropTypeEnum.Bitmap))
                        {
                            OpenFile(args.Data[DropTypeEnum.Bitmap] as MemoryStream);
                        }
                    }
                }));
            }
        }

        #endregion

        #region Public Properties

        /// <inheritdoc/>
        public ISettingsProvider Settings { get; }

        /// <inheritdoc/>
        public IEnumerable<Shortcut> Shortcuts
        {
            get
            {
                yield return Settings.CloseTransparentWindowShortcut;
                yield return Settings.TransparencyShortcut;
            }
        }

        /// <inheritdoc/>
        public ReadOnlyObservableCollection<IImageItem> Images { get => _readOnlyBuff; }

        #endregion

        #region Public Methods

        /// <inheritdoc/>
        public void ProcessShortcut(Shortcut shortcut)
        {
            if (shortcut == null)
                throw new ArgumentNullException("Cannot process null.");

            if (_transparentWindowViewModel == null)
                return;

            if (shortcut.Equals(Settings.CloseTransparentWindowShortcut))
            {
                _dialogService.CloseWindow(_transparentWindowViewModel);
                _transparentWindowViewModel.Dispose();
                _transparentWindowViewModel = null;
            }
            else if (shortcut.Equals(Settings.TransparencyShortcut))
            {
                _transparentWindowViewModel.IsTransparent = !_transparentWindowViewModel.IsTransparent;
            }
            else
            {
                throw new InvalidOperationException("Unknown shortcut");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Opens files. Assumes all pathes is achivable from current destination.
        /// </summary>
        /// <param name="files">Files to open. Must not be null.</param>
        private void OpenFiles(IEnumerable<string> files)
        {
            if (files == null)
                throw new ArgumentNullException($"{nameof(files)} is null.");

            foreach (string file in files)
            {
                _dispatcher.BeginInvoke(() => _images.Add(new ImageItem(file)), DaliDispatcherPriorityEnum.Background);
            }
        }

        /// <summary>
        /// Opens file stored in memory.
        /// </summary>
        /// <param name="stream">Stream that holds memory with file. Must not be null.</param>
        private void OpenFile(MemoryStream stream)
        {
            //I haven't tested it as I haven't found any suitable apps to drag out image as bitmap.
            if (stream == null)
                throw new ArgumentNullException($"{nameof(stream)} is null.");

            //Memory stream does not performs any actions with buffer on disposing,
            //so I think it's safe to do that.
            _images.Add(new ImageItem(stream.GetBuffer()));
        }

        /// <summary>
        /// Checks if data dragged over the list might be opened in application.
        /// </summary>
        /// <param name="args">Cross platform drag and drop event args.</param>
        private void CheckDropData(DragAndDropEventArgs args)
        {
            args.Effects = DragAndDropEffectsEnum.None;
            args.Handled = true;
            if (args.Data.ContainsKey(DropTypeEnum.FilePath))
            {
                string[] files = args.Data[DropTypeEnum.FilePath] as string[];

                if (files != null && files.Any() &&
                    files.All(file => SupportedFormats.Any(ext => ext.Equals(Path.GetExtension(file), StringComparison.OrdinalIgnoreCase))))
                {
                    args.Effects = DragAndDropEffectsEnum.Copy;
                }
            }
            else if (args.Data.ContainsKey(DropTypeEnum.Bitmap))
            {
                args.Effects = DragAndDropEffectsEnum.Copy;
            }
        }

        #endregion

        #region IMainWindowViewModel

        ///<inheritdoc/>
        ICommand IMainWindowViewModel.DragEnterCommand { get => DragEnterCommand; }
        
        ///<inheritdoc/>
        ICommand IMainWindowViewModel.DragOverCommand { get => DragOverCommand; }

        ///<inheritdoc/>
        ICommand IMainWindowViewModel.DropCommand { get => DropCommand; }

        ///<inheritdoc/>
        ICommand IMainWindowViewModel.SaveCommand { get => SaveCommand; }

        ///<inheritdoc/>
        ICommand IMainWindowViewModel.LoadCommand { get => LoadCommand; }

        ///<inheritdoc/>
        ICommand IMainWindowViewModel.RemoveCommand { get => RemoveCommand; }

        ///<inheritdoc/>
        ICommand IMainWindowViewModel.StartCommand { get => StartCommand; }


        #endregion

        #region Disposable
        /// <summary>
        /// Disposes internal subscriptions, reactive commands and opened images.
        /// </summary>
        public void Dispose()
        {
            _imagesSubscription.Dispose();

            _startCommand?.Dispose();
            _loadCommand?.Dispose();
            _removeCommand?.Dispose();
            _dragEnterCommand?.Dispose();
            _dragOverCommand?.Dispose();
            _dropCommand?.Dispose();

            foreach (ImageItem item in _images.Items)
                item.Dispose();

            _images.Dispose();
        }

        #endregion
    }
}
