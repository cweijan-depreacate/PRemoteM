﻿using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Dragablz;
using _1RM.Model;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Utils;
using _1RM.View.Host.ProtocolHosts;
using Shawn.Utils.Interface;
using Shawn.Utils.Wpf;
using Stylet;

namespace _1RM.View.Host
{
    public class TabWindowViewModel : NotifyPropertyChangedBaseScreen, IDisposable
    {
        public readonly string Token;

        public TabWindowViewModel(string token)
        {
            Token = token;
            Items.CollectionChanged += ItemsOnCollectionChanged;
        }

        private void ItemsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(BtnCloseAllVisibility));
            if (Items.Count == 0 && this.View is TabWindowView tab)
            {
                tab.Hide();
            }
        }

        public void Dispose()
        {
            Execute.OnUIThread(() =>
            {
                SelectedItem = null;
                foreach (var item in Items.ToArray())
                {
                    if (item.Content is IDisposable dp)
                    {
                        dp.Dispose();
                    }
                }
                Items.CollectionChanged -= ItemsOnCollectionChanged;
                Items.Clear();
            });
        }

        private string _title = "";
        public string Title
        {
            get => _title;
            set => SetAndNotifyIfChanged(ref _title, value);
        }

        public ResizeMode WindowResizeMode
        {
            get
            {
                if (SelectedItem?.Content == null
                    || (SelectedItem?.Content is AxMsRdpClient09Host && SelectedItem?.CanResizeNow == false))
                    return ResizeMode.NoResize;
                return ResizeMode.CanResize;
            }
        }

        public ObservableCollection<TabItemViewModel> Items { get; } = new ObservableCollection<TabItemViewModel>();

        public Visibility BtnCloseAllVisibility => Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        private TabItemViewModel? _selectedItem = null;
        public TabItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != null)
                    try
                    {
                        _selectedItem.PropertyChanged -= SelectedItemOnPropertyChanged;
                    }
                    catch (Exception e)
                    {
                        Console.Write(e);
                    }

                if (SetAndNotifyIfChanged(ref _selectedItem, value))
                {
                    RaisePropertyChanged(nameof(WindowResizeMode));

                    if (_selectedItem != null)
                    {
                        SetTitle();
                        _selectedItem.PropertyChanged += SelectedItemOnPropertyChanged;
                    }
                }
            }
        }

        private void SelectedItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TabItemViewModel.CanResizeNow))
            {
                RaisePropertyChanged(nameof(WindowResizeMode));
            }
        }

        #region drag drop tab

        public IInterTabClient InterTabClient { get; } = new InterTabClient();

        #endregion drag drop tab

        private void SetTitle()
        {
            if (SelectedItem != null)
            {
                this.Title = SelectedItem.Header + " - " + AppPathHelper.APP_DISPLAY_NAME;
            }
        }

        #region CMD

        private RelayCommand? _cmdHostGoFullScreen;
        public RelayCommand CmdHostGoFullScreen
        {
            get
            {
                return _cmdHostGoFullScreen ??= new RelayCommand((o) =>
                {
                    if (this.SelectedItem?.Content?.CanResizeNow() ?? false)
                        IoC.Get<SessionControlService>().MoveProtocolHostToFullScreen(SelectedItem.Content.ConnectionId);
                }, o => this.SelectedItem != null && (this.SelectedItem.Content?.CanFullScreen ?? false));
            }
        }

        private RelayCommand? _cmdInvokeLauncher;
        public RelayCommand CmdInvokeLauncher
        {
            get
            {
                return _cmdInvokeLauncher ??= new RelayCommand((o) => { IoC.Get<LauncherWindowViewModel>().ShowMe(); }, o => this.SelectedItem != null);
            }
        }

        private RelayCommand? _cmdShowTabByIndex;
        public RelayCommand CmdShowTabByIndex
        {
            get
            {
                return _cmdShowTabByIndex ??= new RelayCommand((o) =>
                {
                    if (int.TryParse(o?.ToString() ?? "0", out int i))
                    {
                        if (i > 0 && i <= Items.Count)
                        {
                            SelectedItem = Items[i - 1];
                        }
                    }
                }, o => this.SelectedItem != null);
            }
        }

        private RelayCommand? _cmdGoMinimize;
        public RelayCommand CmdGoMinimize
        {
            get
            {
                return _cmdGoMinimize ??= new RelayCommand((o) =>
                {
                    if (o is Window window)
                    {
                        window.WindowState = WindowState.Minimized;
                        if (SelectedItem?.Content != null)
                            SelectedItem.Content.ToggleAutoResize(false);
                    }
                });
            }
        }

        private RelayCommand? _cmdGoMaximize;
        public RelayCommand CmdGoMaximize
        {
            get
            {
                return _cmdGoMaximize ??= new RelayCommand((o) =>
                {
                    if (o is Window window)
                        window.WindowState = (window.WindowState == WindowState.Normal) ? WindowState.Maximized : WindowState.Normal;
                });
            }
        }

        private bool _canCmdClose = true;

        private RelayCommand? _cmdCloseAll;
        public RelayCommand CmdCloseAll
        {
            get
            {
                return _cmdCloseAll ??= new RelayCommand((o) =>
                {
                    if (_canCmdClose)
                    {
                        _canCmdClose = false;
                        if (IoC.Get<ConfigurationService>().General.ConfirmBeforeClosingSession == true
                            && this.Items.Count > 0
                            && false == MessageBoxHelper.Confirm(IoC.Get<ILanguageService>().Translate("Are you sure you want to close the connection?")))
                        {
                        }
                        else
                        {
                            IoC.Get<SessionControlService>().CloseProtocolHostAsync(
                                Items
                                .Where(x => x.Host.ProtocolServer.IsTmpSession() == false)
                                .Select(x => x.Host.ConnectionId).ToArray());
                        }
                        _canCmdClose = true;
                    }
                });
            }
        }

        private RelayCommand? _cmdClose;
        public RelayCommand CmdClose
        {
            get
            {
                return _cmdClose ??= new RelayCommand((o) =>
                {
                    if (_canCmdClose)
                    {
                        _canCmdClose = false;
                        if (IoC.Get<ConfigurationService>().General.ConfirmBeforeClosingSession == true
                            && false == MessageBoxHelper.Confirm(IoC.Get<ILanguageService>().Translate("Are you sure you want to close the connection?")))
                        {
                        }
                        else
                        {
                            HostBase? host = null;
                            if (o is string connectionId)
                            {
                                host = Items.FirstOrDefault(x => x.Host.ConnectionId == connectionId)?.Host;
                            }
                            else
                            {
                                host = SelectedItem?.Content;
                            }

                            if (host != null)
                            {
                                IoC.Get<SessionControlService>().CloseProtocolHostAsync(host.ConnectionId);
                            }
                        }

                        _canCmdClose = true;
                    }
                }, o => this.SelectedItem != null);
            }
        }

        #endregion CMD
    }

    public class InterTabClient : IInterTabClient
    {
        /// <summary>
        /// split tab window
        /// </summary>
        /// <param name="interTabClient"></param>
        /// <param name="partition"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        public INewTabHost<Window> GetNewHost(IInterTabClient interTabClient, object partition, TabablzControl source)
        {
            string token = DateTime.Now.Ticks.ToString();
            var v = new TabWindowView(token, IoC.Get<LocalityService>());
            IoC.Get<SessionControlService>().AddTab(v);
            return new NewTabHost<Window>(v, v.TabablzControl);
        }

        /// <summary>
        /// merge tab window
        /// </summary>
        /// <param name="tabControl"></param>
        /// <param name="window"></param>
        /// <returns></returns>
        public TabEmptiedResponse TabEmptiedHandler(TabablzControl tabControl, Window window)
        {
            if (window is TabWindowBase tab)
            {
                tab.GetViewModel().Items.Clear();
                IoC.Get<SessionControlService>().CleanupProtocolsAndWindows();
            }
            return TabEmptiedResponse.CloseWindowOrLayoutBranch;
        }
    }
}