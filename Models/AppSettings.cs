using System;

namespace DiskChecker.Models
{
    public class AppSettings
    {
        public string AppTitle { get; set; } = "DiskChecker Дашборд";
        public double DefaultMinSizeMB { get; set; } = 10;
        public int DefaultMaxDepth { get; set; } = 3;
    }
}
