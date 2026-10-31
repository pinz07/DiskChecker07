using System;
using System.Collections.Generic;

namespace DiskChecker.Models
{
    /// <summary>
    /// Модель данных для элемента файловой системы (папки или файла).
    /// </summary>
    public class FileSystemItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public int Level { get; set; }
        public bool IsDirectory { get; set; } = true;

        // ── Dates (populated during scan) ──────────────────────────
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public DateTime LastAccessTime { get; set; }

        public System.Windows.Rect Rect { get; set; }
        public List<FileSystemItem> Children { get; set; } = new();

        public double SizeMB => Size / (1024.0 * 1024.0);
        public double SizeGB => Size / (1024.0 * 1024.0 * 1024.0);

        public string FormattedSize =>
            Size >= 1_073_741_824L ? $"{SizeGB:F2} ГБ" :
            Size >= 1_048_576L ? $"{SizeMB:F2} МБ" :
            Size >= 1024L ? $"{Size / 1024.0:F2} КБ" :
            $"{Size} Б";
            
        public string Icon => IsDirectory ? "📁" : "📄";
        public string TypeName => IsDirectory ? "Папка" : "Файл";
    }

    /// <summary>
    /// Элемент визуализации Treemap.
    /// </summary>
    public class TreemapRectItem
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public System.Windows.Media.Brush? Fill { get; set; }
        public System.Windows.Media.Brush? BorderBrush { get; set; }
        public double BorderThickness { get; set; } = 1;
        
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string FormattedSize { get; set; } = string.Empty;
        public int Level { get; set; }
        public bool IsDirectory { get; set; }
        public string TypeName => IsDirectory ? "Папка" : "Файл";

        public bool ShowLabel => Width > 60 && Height > 24;
        public double FontSize => Math.Max(9, Math.Min(14, Math.Min(Width, Height) / 6));
    }
}
