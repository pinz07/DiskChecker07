using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Models;

namespace DiskChecker.ViewModels
{
    // ────────────────────────────────────────────────────────────────────────────
    //  Flat DTO that DataGrid is bound to
    // ────────────────────────────────────────────────────────────────────────────
    public class FileItemViewModel
    {
        public string Name         { get; init; } = string.Empty;
        public string Path         { get; init; } = string.Empty;
        public double SizeMB       { get; init; }
        public DateTime CreatedAt  { get; init; }
        public DateTime ModifiedAt { get; init; }
        public DateTime AccessedAt { get; init; }

        /// <summary>Human-readable size string shown in tooltip / extra column.</summary>
        public string FormattedSize =>
            SizeMB >= 1024   ? $"{SizeMB / 1024:F2} ГБ" :
            SizeMB >= 1      ? $"{SizeMB:F2} МБ"        :
            SizeMB >= 0.001  ? $"{SizeMB * 1024:F2} КБ" :
                               $"{(long)(SizeMB * 1024 * 1024)} Б";
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Filter type enum
    // ────────────────────────────────────────────────────────────────────────────
    public enum FilterType
    {
        RarelyUsed,   // по дате последнего доступа
        OldFiles,     // по дате создания / изменения
        LargeFiles,   // размер > порога
        Junk          // «мусорные» расширения
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  ViewModel
    // ────────────────────────────────────────────────────────────────────────────
    public partial class SmartFilterViewModel : ObservableObject
    {
        // ── Shared reference from MainViewModel ─────────────────────────────────
        private FileSystemItem? _rootFolder;
        private CancellationTokenSource? _filterCts;

        // ── Filter parameters ───────────────────────────────────────────────────
        [ObservableProperty]
        private FilterType _selectedFilter = FilterType.LargeFiles;

        [ObservableProperty]
        private int _daysThreshold = 180;       // RarelyUsed / OldFiles

        [ObservableProperty]
        private double _minSizeMBThreshold = 100; // LargeFiles

        // ── State ────────────────────────────────────────────────────────────────
        [ObservableProperty]
        private bool _isFiltering;

        [ObservableProperty]
        private string _statusText = "Сначала нажмите «Применить фильтр»";

        [ObservableProperty]
        private int _resultCount;

        // ── Progress & Stage tracking ───────────────────────────────────────────
        [ObservableProperty]
        private double _filterProgress; // 0..100

        [ObservableProperty]
        private string _progressStageText = string.Empty; // "Этап 2 из 3: Анализ файлов"

        [ObservableProperty]
        private string _estimatedTimeText = string.Empty; // "Осталось: ~3 сек"

        [ObservableProperty]
        private string _progressDetailsText = string.Empty; // "Обработано: 45 200 из 120 000"

        // ── Results ──────────────────────────────────────────────────────────────
        [ObservableProperty]
        private ObservableCollection<FileItemViewModel> _filteredFiles = new();

        // ── "Junk" extensions ────────────────────────────────────────────────────
        private static readonly HashSet<string> JunkExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tmp", ".temp", ".log", ".bak", ".old", ".cache",
            ".dmp", ".chk", ".~*", ".thumbs", ".ds_store",
            ".suo", ".user", ".ncb", ".aps", ".ilk", ".pdb",
            ".bk", ".orig", ".swp", ".lock", ".crdownload"
        };

        // ── Filter descriptions for the status bar ───────────────────────────────
        public string FilterDescription => SelectedFilter switch
        {
            FilterType.RarelyUsed  => $"Давно не использовались (> {DaysThreshold} дней)",
            FilterType.OldFiles    => $"Старые файлы (созданы / изменены > {DaysThreshold} дней назад)",
            FilterType.LargeFiles  => $"Большие файлы (> {MinSizeMBThreshold:F0} МБ)",
            FilterType.Junk        => "Возможный мусор (.tmp, .log, .bak …)",
            _                      => string.Empty
        };

        // ────────────────────────────────────────────────────────────────────────
        //  Public API — called by MainViewModel after scan completes
        // ────────────────────────────────────────────────────────────────────────
        public void SetRootFolder(FileSystemItem root)
        {
            _rootFolder = root;
            StatusText  = "Данные получены. Нажмите «Применить фильтр».";
        }

        // ────────────────────────────────────────────────────────────────────────
        //  Commands
        // ────────────────────────────────────────────────────────────────────────
        [RelayCommand]
        private async Task ApplyFilterAsync()
        {
            if (_rootFolder == null)
            {
                StatusText = "⚠ Сначала выполните сканирование диска.";
                return;
            }

            _filterCts?.Cancel();
            _filterCts?.Dispose();
            _filterCts = new CancellationTokenSource();
            var ct = _filterCts.Token;

            IsFiltering = true;
            FilterProgress = 0;
            ProgressStageText = "Этап 1 из 3: Подготовка данных...";
            EstimatedTimeText = "Оценка времени...";
            ProgressDetailsText = "Подсчет файлов в памяти...";
            StatusText = $"🔍 Анализ структуры: {FilterDescription} …";
            FilteredFiles.Clear();
            ResultCount = 0;

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Capture parameters for the background thread
                var filterType   = SelectedFilter;
                var days         = DaysThreshold;
                var minSizeBytes = (long)(MinSizeMBThreshold * 1024 * 1024);
                var now          = DateTime.Now;
                var root         = _rootFolder;
                var filterDesc   = FilterDescription;

                var results = await Task.Run(() =>
                {
                    // ── ЭТАП 1: Быстрый подсчет общего количества файлов ──────
                    int totalFiles = CountFilesInTree(root, ct);
                    if (ct.IsCancellationRequested) return new List<FileItemViewModel>();

                    // UI update for stage 1 finish
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        FilterProgress = 10;
                        ProgressStageText = $"Этап 2 из 3: Анализ файлов ({filterDesc})";
                        ProgressDetailsText = $"Всего в памяти: {totalFiles:N0} файлов. Начало фильтрации...";
                    });

                    // ── ЭТАП 2: Итеративный обход и фильтрация без лишних аллокаций ──
                    var matched = new List<FileItemViewModel>(Math.Min(totalFiles, 4096));
                    var stack = new Stack<FileSystemItem>();
                    stack.Push(root);

                    int processed = 0;
                    long lastReportTicks = stopwatch.ElapsedTicks;

                    while (stack.Count > 0)
                    {
                        if (ct.IsCancellationRequested) return matched;
                        var current = stack.Pop();

                        foreach (var child in current.Children)
                        {
                            if (ct.IsCancellationRequested) return matched;

                            if (child.IsDirectory)
                            {
                                stack.Push(child);
                            }
                            else
                            {
                                processed++;

                                bool isMatch = filterType switch
                                {
                                    FilterType.RarelyUsed => (now - child.LastAccessTime).TotalDays > days,
                                    FilterType.OldFiles   => (now - child.LastWriteTime).TotalDays > days &&
                                                             (now - child.CreationTime).TotalDays > days,
                                    FilterType.LargeFiles => child.Size >= minSizeBytes,
                                    FilterType.Junk       => JunkExtensions.Contains(System.IO.Path.GetExtension(child.Name)),
                                    _                     => true
                                };

                                if (isMatch)
                                {
                                    matched.Add(new FileItemViewModel
                                    {
                                        Name       = child.Name,
                                        Path       = child.Path,
                                        SizeMB     = child.Size / (1024.0 * 1024.0),
                                        CreatedAt  = child.CreationTime,
                                        ModifiedAt = child.LastWriteTime,
                                        AccessedAt = child.LastAccessTime
                                    });
                                }

                                // Дросселирование обновления UI: раз в 50мс или каждые 1500 файлов
                                long currentTicks = stopwatch.ElapsedTicks;
                                if (currentTicks - lastReportTicks > Stopwatch.Frequency / 20 || processed % 2000 == 0)
                                {
                                    lastReportTicks = currentTicks;
                                    double fraction = totalFiles > 0 ? (double)processed / totalFiles : 0.5;
                                    double pct = 10.0 + fraction * 70.0; // от 10% до 80%

                                    double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                                    string remainingStr = "Оценка времени...";
                                    if (elapsedSec > 0.2 && fraction > 0.02)
                                    {
                                        double estTotalSec = elapsedSec / fraction;
                                        double secLeft = estTotalSec - elapsedSec;
                                        remainingStr = FormatRemainingTime(secLeft);
                                    }

                                    int curProcessed = processed;
                                    int curMatched = matched.Count;

                                    Application.Current?.Dispatcher.InvokeAsync(() =>
                                    {
                                        FilterProgress = Math.Min(pct, 80);
                                        ProgressStageText = $"Этап 2 из 3: Анализ файлов ({filterDesc})";
                                        EstimatedTimeText = remainingStr;
                                        ProgressDetailsText = $"Обработано: {curProcessed:N0} из {totalFiles:N0} • Найдено: {curMatched:N0}";
                                    });
                                }
                            }
                        }
                    }

                    if (ct.IsCancellationRequested) return matched;

                    // ── ЭТАП 3: Сортировка результатов ───────────────────────
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        FilterProgress = 85;
                        ProgressStageText = "Этап 3 из 3: Сортировка и подготовка результатов...";
                        EstimatedTimeText = "Завершение...";
                        ProgressDetailsText = $"Сортировка {matched.Count:N0} найденных файлов...";
                    });

                    IEnumerable<FileItemViewModel> sorted = filterType switch
                    {
                        FilterType.LargeFiles => matched.OrderByDescending(f => f.SizeMB),
                        FilterType.RarelyUsed => matched.OrderBy(f => f.AccessedAt),
                        FilterType.OldFiles   => matched.OrderBy(f => f.ModifiedAt),
                        FilterType.Junk       => matched.OrderByDescending(f => f.SizeMB),
                        _                     => matched
                    };

                    return sorted.ToList();

                }, ct);

                if (!ct.IsCancellationRequested)
                {
                    FilterProgress = 95;
                    ProgressStageText = "Обновление таблицы результатов...";
                    ProgressDetailsText = $"Загрузка {results.Count:N0} элементов...";

                    FilteredFiles = new ObservableCollection<FileItemViewModel>(results);
                    ResultCount   = FilteredFiles.Count;

                    FilterProgress = 100;
                    ProgressStageText = "✅ Анализ завершен!";
                    EstimatedTimeText = $"Заняло {stopwatch.Elapsed.TotalSeconds:F1} сек";

                    StatusText = ResultCount > 0
                        ? $"✅ Найдено {ResultCount:N0} файлов — {FilterDescription} (за {stopwatch.Elapsed.TotalSeconds:F1} сек)"
                        : $"ℹ Ничего не найдено — {FilterDescription}";
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "Фильтрация отменена пользователем.";
                ProgressStageText = "Отменено";
                EstimatedTimeText = string.Empty;
                ProgressDetailsText = "Процесс был прерван.";
            }
            catch (Exception ex)
            {
                StatusText = $"❌ Ошибка: {ex.Message}";
                ProgressStageText = "Ошибка выполнения";
                EstimatedTimeText = string.Empty;
            }
            finally
            {
                stopwatch.Stop();
                IsFiltering = false;
            }
        }

        [RelayCommand]
        private void CancelFilter()
        {
            _filterCts?.Cancel();
        }

        [RelayCommand]
        private void OpenInExplorer(FileItemViewModel? item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path)) return;
            try
            {
                if (File.Exists(item.Path))
                    Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
                else if (Directory.Exists(item.Path))
                    Process.Start("explorer.exe", item.Path);
            }
            catch (Exception ex)
            {
                StatusText = $"❌ Не удалось открыть: {ex.Message}";
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  Helper: быстрый и безопасный подсчет файлов в дереве
        // ────────────────────────────────────────────────────────────────────────
        private static int CountFilesInTree(FileSystemItem root, CancellationToken ct)
        {
            int count = 0;
            var stack = new Stack<FileSystemItem>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) return count;
                var current = stack.Pop();
                foreach (var child in current.Children)
                {
                    if (child.IsDirectory)
                        stack.Push(child);
                    else
                        count++;
                }
            }
            return count;
        }

        // ────────────────────────────────────────────────────────────────────────
        //  Helper: форматирование оставшегося времени
        // ────────────────────────────────────────────────────────────────────────
        private static string FormatRemainingTime(double secondsLeft)
        {
            if (secondsLeft < 1)
                return "менее 1 сек";
            if (secondsLeft < 60)
                return $"~{(int)Math.Ceiling(secondsLeft)} сек";
            int mins = (int)(secondsLeft / 60);
            int secs = (int)(secondsLeft % 60);
            return $"~{mins} мин {secs} сек";
        }
    }
}
