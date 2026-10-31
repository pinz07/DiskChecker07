using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Layout;
using DiskChecker.Models;

namespace DiskChecker.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // ===== Drives =====
        [ObservableProperty]
        private ObservableCollection<string> _availableDrives = new();

        [ObservableProperty]
        private string? _selectedDrive;
        
        [ObservableProperty]
        private string _appTitle = "DiskChecker Дашборд";

        // ===== Scan State =====
        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private string _statusText = "Выберите диск для сканирования";

        [ObservableProperty]
        private double _scanProgress;

        [ObservableProperty]
        private string _scanProgressText = "";

        private FileSystemItem? _rootFolder;

        // ===== Filters =====
        [ObservableProperty]
        private double _minSizeMB = 10;

        [ObservableProperty]
        private int _maxDepth = 3;

        [ObservableProperty]
        private string _searchFilter = "";

        // ===== Visualizations =====
        [ObservableProperty]
        private ObservableCollection<TreemapRectItem> _treemapItems = new();

        [ObservableProperty]
        private double _canvasWidth = 600; // Will be set from UI

        [ObservableProperty]
        private double _canvasHeight = 400; // Will be set from UI

        [ObservableProperty]
        private ObservableCollection<FileSystemItem> _treeViewItems = new();

        // ===== Top Cards Data =====
        [ObservableProperty]
        private string _totalSpaceStr = "0 Б";
        
        [ObservableProperty]
        private string _usedSpaceStr = "0 Б";
        [ObservableProperty]
        private double _usedSpacePercent = 0;

        [ObservableProperty]
        private string _freeSpaceStr = "0 Б";
        [ObservableProperty]
        private double _freeSpacePercent = 0;

        [ObservableProperty]
        private string _scannedCountStr = "0";

        // ===== Upcoming Bills (Top 5 Largest Folders) =====
        [ObservableProperty]
        private ObservableCollection<FileSystemItem> _topLargestItems = new();

        [ObservableProperty]
        private int _currentViewIndex = 0; // 0: Dashboard, 1: Treemap, 2: List, 3: SmartFilter

        // ── Smart Filter sub-ViewModel ──────────────────────────────────────────
        public SmartFilterViewModel SmartFilter { get; } = new SmartFilterViewModel();

        private CancellationTokenSource? _scanCts;
        private System.Timers.Timer? _searchDebounceTimer;

        public MainViewModel()
        {
            LoadSettings();
            LoadDrives();
        }

        private void LoadSettings()
        {
            try
            {
                string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        AppTitle = settings.AppTitle;
                        MinSizeMB = settings.DefaultMinSizeMB;
                        MaxDepth = settings.DefaultMaxDepth;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка загрузки настроек: {ex.Message}";
            }
        }

        [RelayCommand]
        private void SetView(string viewName)
        {
            if (viewName == "Dashboard")    CurrentViewIndex = 0;
            else if (viewName == "Treemap") CurrentViewIndex = 1;
            else if (viewName == "List")    CurrentViewIndex = 2;
            else if (viewName == "SmartFilter") CurrentViewIndex = 3;
        }

        [RelayCommand]
        private void CancelScan()
        {
            _scanCts?.Cancel();
            IsScanning = false;
            StatusText = "Сканирование прервано пользователем";
        }

        private void LoadDrives()
        {
            AvailableDrives.Clear();
            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    AvailableDrives.Add(drive.Name);
                }
            }
        }

        partial void OnSelectedDriveChanged(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _ = ScanDriveAsync(value);
            }
        }

        partial void OnMinSizeMBChanged(double value) => RebuildVisualization();
        partial void OnMaxDepthChanged(int value) => RebuildVisualization();
        partial void OnCanvasWidthChanged(double value) => RebuildTreemap();
        partial void OnCanvasHeightChanged(double value) => RebuildTreemap();

        partial void OnSearchFilterChanged(string value)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer?.Dispose();
            _searchDebounceTimer = new System.Timers.Timer(300) { AutoReset = false };
            _searchDebounceTimer.Elapsed += (s, e) =>
            {
                Application.Current?.Dispatcher.Invoke(RebuildVisualization);
            };
            _searchDebounceTimer.Start();
        }

        private async Task ScanDriveAsync(string drivePath)
        {
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;

            IsScanning = true;
            StatusText = $"Сканирование {drivePath} ...";
            ScanProgress = 0;
            ScanProgressText = "Подготовка...";
            _rootFolder = null;
            TreemapItems.Clear();
            TreeViewItems.Clear();
            TopLargestItems.Clear();

            try
            {
                var di = new System.IO.DriveInfo(drivePath.TrimEnd('\\'));
                TotalSpaceStr = FormatSize(di.TotalSize);
                FreeSpaceStr = FormatSize(di.AvailableFreeSpace);
                UsedSpaceStr = FormatSize(di.TotalSize - di.AvailableFreeSpace);
                
                UsedSpacePercent = di.TotalSize > 0 ? (double)(di.TotalSize - di.AvailableFreeSpace) / di.TotalSize * 100 : 0;
                FreeSpacePercent = di.TotalSize > 0 ? (double)di.AvailableFreeSpace / di.TotalSize * 100 : 0;
            }
            catch { }

            try
            {
                int scannedCount = 0;

                _rootFolder = await Task.Run(() =>
                {
                    var root = new FileSystemItem
                    {
                        Name = drivePath,
                        Path = drivePath,
                        Level = 0,
                        IsDirectory = true
                    };

                    ScanDirectory(root, drivePath, 0, token, ref scannedCount);
                    return root;
                }, token);

                if (token.IsCancellationRequested) return;

                ScannedCountStr = scannedCount.ToString("N0");
                StatusText = $"✅ Обновлено сегодня";
                ScanProgress = 100;

                // Передаём дерево в SmartFilterViewModel
                SmartFilter.SetRootFolder(_rootFolder);

                RebuildVisualization();
            }
            catch (OperationCanceledException)
            {
                StatusText = "Сканирование отменено";
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        private void ScanDirectory(FileSystemItem parent, string path, int level, CancellationToken token, ref int scannedCount)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path))
                    {
                        if (token.IsCancellationRequested) return;
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            var fileItem = new FileSystemItem
                            {
                                Name = fileInfo.Name,
                                Path = fileInfo.FullName,
                                Size = fileInfo.Length,
                                Level = level + 1,
                                IsDirectory = false,
                                CreationTime = fileInfo.CreationTime,
                                LastWriteTime = fileInfo.LastWriteTime,
                                LastAccessTime = fileInfo.LastAccessTime
                            };
                            parent.Children.Add(fileItem);
                            parent.Size += fileItem.Size;
                            scannedCount++;
                        }
                        catch (UnauthorizedAccessException) { }
                        catch (PathTooLongException) { }
                        catch (IOException) { }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (PathTooLongException) { }
                catch (IOException) { }

                IEnumerable<string> subdirs;
                try
                {
                    subdirs = Directory.EnumerateDirectories(path);
                }
                catch (UnauthorizedAccessException) { return; }
                catch (PathTooLongException) { return; }
                catch (IOException) { return; }

                foreach (var subdir in subdirs)
                {
                    if (token.IsCancellationRequested) return;

                    try
                    {
                        var dirName = System.IO.Path.GetFileName(subdir);
                        var child = new FileSystemItem
                        {
                            Name = dirName,
                            Path = subdir,
                            Level = level + 1,
                            IsDirectory = true
                        };

                        ScanDirectory(child, subdir, level + 1, token, ref scannedCount);
                        parent.Children.Add(child);
                        parent.Size += child.Size;
                        scannedCount++;

                        if (scannedCount % 500 == 0)
                        {
                            var count = scannedCount;
                            Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                ScanProgress = (count % 2000) / 2000.0 * 95;
                                ScannedCountStr = count.ToString("N0");
                            });
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (PathTooLongException) { }
                    catch (IOException) { }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (IOException) { }
        }

        private void RebuildVisualization()
        {
            if (_rootFolder == null) return;
            RebuildTreemap();
            RebuildTreeView();
            UpdateTopLargest();
        }

        private void RebuildTreemap()
        {
            if (_rootFolder == null || CanvasWidth <= 0 || CanvasHeight <= 0) return;

            long minSizeBytes = (long)(MinSizeMB * 1024 * 1024);
            var items = TreemapLayout.BuildTreemap(
                _rootFolder,
                CanvasWidth,
                CanvasHeight,
                MaxDepth,
                minSizeBytes,
                SearchFilter);

            TreemapItems = new ObservableCollection<TreemapRectItem>(items);
        }

        private void RebuildTreeView()
        {
            if (_rootFolder == null) return;

            long minSizeBytes = (long)(MinSizeMB * 1024 * 1024);
            var filtered = FilterTreeForView(_rootFolder, minSizeBytes, MaxDepth, 0, SearchFilter);
            TreeViewItems = new ObservableCollection<FileSystemItem>(filtered);
        }

        private void UpdateTopLargest()
        {
            if (_rootFolder == null) return;
            long minSizeBytes = (long)(MinSizeMB * 1024 * 1024);
            
            // Get flat list of all nodes matching depth and minSize
            var flatList = new List<FileSystemItem>();
            FlattenTree(_rootFolder, flatList, minSizeBytes, MaxDepth, 0);

            var top5 = flatList
                .Where(x => x.Name != _rootFolder.Name) // Exclude root
                .OrderByDescending(x => x.Size)
                .Take(5)
                .ToList();

            TopLargestItems = new ObservableCollection<FileSystemItem>(top5);
        }
        
        private void FlattenTree(FileSystemItem node, List<FileSystemItem> result, long minSizeBytes, int maxDepth, int currentDepth)
        {
            if (node.Size < minSizeBytes) return;
            
            result.Add(node);
            if (node.IsDirectory && currentDepth < maxDepth - 1)
            {
                foreach (var child in node.Children)
                {
                    FlattenTree(child, result, minSizeBytes, maxDepth, currentDepth + 1);
                }
            }
        }

        private List<FileSystemItem> FilterTreeForView(FileSystemItem parent, long minSizeBytes, int maxDepth, int currentDepth, string search)
        {
            var result = new List<FileSystemItem>();

            var children = parent.Children
                .Where(c => c.Size >= minSizeBytes)
                .OrderByDescending(c => c.Size)
                .ThenBy(c => c.Name);

            if (!string.IsNullOrWhiteSpace(search))
            {
                children = children.Where(c => ContainsNameRecursive(c, search))
                    .OrderByDescending(c => c.Size)
                    .ThenBy(c => c.Name);
            }

            foreach (var child in children)
            {
                var clone = new FileSystemItem
                {
                    Name = child.Name,
                    Path = child.Path,
                    Size = child.Size,
                    Level = child.Level,
                    IsDirectory = child.IsDirectory
                };

                if (child.IsDirectory && currentDepth < maxDepth - 1)
                {
                    clone.Children = FilterTreeForView(child, minSizeBytes, maxDepth, currentDepth + 1, search);
                }

                result.Add(clone);
            }

            return result;
        }

        private bool ContainsNameRecursive(FileSystemItem item, string filter)
        {
            if (item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
            if (item.IsDirectory)
                return item.Children.Any(c => ContainsNameRecursive(c, filter));
            return false;
        }

        [RelayCommand]
        private void OpenInExplorer(FileSystemItem? item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path)) return;
            try
            {
                if (item.IsDirectory && Directory.Exists(item.Path))
                    Process.Start("explorer.exe", item.Path);
                else if (!item.IsDirectory && File.Exists(item.Path))
                    Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
            }
            catch (Exception ex)
            {
                StatusText = $"❌ Не удалось открыть: {ex.Message}";
            }
        }
        
        [RelayCommand]
        private void OpenInExplorerTreemap(TreemapRectItem? item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path)) return;
            try
            {
                if (item.IsDirectory && Directory.Exists(item.Path))
                    Process.Start("explorer.exe", item.Path);
                else if (!item.IsDirectory && File.Exists(item.Path))
                    Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
            }
            catch (Exception ex)
            {
                StatusText = $"❌ Не удалось открыть: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task RescanAsync()
        {
            if (!string.IsNullOrEmpty(SelectedDrive))
                await ScanDriveAsync(SelectedDrive);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1_073_741_824L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} ГБ";
            if (bytes >= 1_048_576L) return $"{bytes / (1024.0 * 1024.0):F2} МБ";
            if (bytes >= 1024L) return $"{bytes / 1024.0:F2} КБ";
            return $"{bytes} Б";
        }
    }
}
