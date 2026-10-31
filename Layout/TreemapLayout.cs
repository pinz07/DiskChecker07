using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using DiskChecker.Models;

namespace DiskChecker.Layout
{
    public static class TreemapLayout
    {
        private static readonly Color[] FolderColors = new[]
        {
            Color.FromRgb(0x1B, 0x3A, 0x4B), // Deep Teal/Blue
            Color.FromRgb(0x4B, 0x21, 0x2A), // Dark Crimson/Wine
            Color.FromRgb(0x27, 0x48, 0x2D), // Deep Forest Green
            Color.FromRgb(0x3B, 0x1F, 0x4B), // Deep Indigo
            Color.FromRgb(0x4F, 0x35, 0x19), // Dark Bronze/Amber
            Color.FromRgb(0x18, 0x40, 0x3D), // Dark Cyan
            Color.FromRgb(0x4B, 0x1B, 0x40), // Dark Berry/Magenta
            Color.FromRgb(0x2F, 0x2A, 0x4A), // Slate Purple
            Color.FromRgb(0x4A, 0x40, 0x1C), // Deep Olive
            Color.FromRgb(0x20, 0x3B, 0x5C), // Navy Blue
        };

        // File colors based roughly on hash or just random bright accents
        private static readonly Color[] FileColors = new[]
        {
            Color.FromRgb(0x00, 0xD2, 0xFF),  // Cyan
            Color.FromRgb(0x7B, 0x2F, 0xF7),  // Purple
            Color.FromRgb(0xFF, 0x6B, 0x6B),  // Coral
            Color.FromRgb(0x48, 0xDB, 0xFB),  // Light blue
            Color.FromRgb(0xA8, 0xEB, 0x12),  // Lime
            Color.FromRgb(0xFF, 0x9F, 0x43),  // Orange
        };

        public static List<TreemapRectItem> BuildTreemap(
            FileSystemItem root,
            double canvasWidth,
            double canvasHeight,
            int maxDepth,
            long minSizeBytes,
            string searchFilter)
        {
            var result = new List<TreemapRectItem>();

            if (root == null || root.Size == 0 || canvasWidth <= 0 || canvasHeight <= 0)
                return result;

            var children = GetFilteredChildren(root, minSizeBytes, searchFilter);
            if (children.Count == 0) return result;

            var rect = new Rect(0, 0, canvasWidth, canvasHeight);
            LayoutRecursive(children, rect, 0, maxDepth, minSizeBytes, searchFilter, result);

            return result;
        }

        private static void LayoutRecursive(
            List<FileSystemItem> items,
            Rect bounds,
            int currentLevel,
            int maxDepth,
            long minSizeBytes,
            string searchFilter,
            List<TreemapRectItem> result)
        {
            if (items.Count == 0 || bounds.Width < 2 || bounds.Height < 2)
                return;

            var rects = Squarify(items, bounds);

            for (int i = 0; i < rects.Count; i++)
            {
                var item = items[i];
                var r = rects[i];

                if (r.Width < 1 || r.Height < 1) continue;

                // Create visual element
                Color baseColor;
                if (!item.IsDirectory)
                {
                    // For files use bright file colors based on name hash
                    int hash = Math.Abs(item.Name.GetHashCode());
                    baseColor = FileColors[hash % FileColors.Length];
                }
                else
                {
                    // For directories use darker foundation colors
                    baseColor = FolderColors[currentLevel % FolderColors.Length];
                    // small variation
                    int variation = (i * 7) % 20 - 10;
                    baseColor = Color.FromRgb(
                        ClampByte(baseColor.R + variation),
                        ClampByte(baseColor.G + variation),
                        ClampByte(baseColor.B + variation));
                }

                var fillBrush = new SolidColorBrush(baseColor) { Opacity = item.IsDirectory ? 0.9 : 0.8 };
                var borderBrush = item.IsDirectory ? new SolidColorBrush(Color.FromRgb(50, 50, 80)) : new SolidColorBrush(DarkenColor(baseColor, 0.3));
                var borderThickness = item.IsDirectory ? Math.Max(0.5, 2.0 - currentLevel * 0.4) : 0.5;

                var rectItem = new TreemapRectItem
                {
                    X = r.X,
                    Y = r.Y,
                    Width = r.Width,
                    Height = r.Height,
                    Fill = fillBrush,
                    BorderBrush = borderBrush,
                    BorderThickness = borderThickness,
                    Name = item.Name,
                    Path = item.Path,
                    Size = item.Size,
                    FormattedSize = item.FormattedSize,
                    Level = currentLevel,
                    IsDirectory = item.IsDirectory
                };
                result.Add(rectItem);

                // Recurse into subdirectories if depth allows
                if (item.IsDirectory && currentLevel < maxDepth - 1)
                {
                    var children = GetFilteredChildren(item, minSizeBytes, searchFilter);
                    if (children.Count > 0)
                    {
                        double padding = Math.Max(1, 4.0 - currentLevel * 0.5);
                        var innerRect = new Rect(
                            r.X + padding,
                            r.Y + padding,
                            Math.Max(0, r.Width - padding * 2),
                            Math.Max(0, r.Height - padding * 2));

                        if (innerRect.Width > 4 && innerRect.Height > 4)
                        {
                            LayoutRecursive(children, innerRect, currentLevel + 1, maxDepth, minSizeBytes, searchFilter, result);
                        }
                    }
                }
            }
        }

        private static List<Rect> Squarify(List<FileSystemItem> items, Rect bounds)
        {
            var result = new Rect[items.Count];
            if (items.Count == 0) return result.ToList();

            double totalSize = items.Sum(i => (double)i.Size);
            if (totalSize <= 0)
            {
                for (int i = 0; i < items.Count; i++)
                    result[i] = new Rect(bounds.X, bounds.Y, 0, 0);
                return result.ToList();
            }

            double totalArea = bounds.Width * bounds.Height;
            var areas = items.Select(i => (double)i.Size / totalSize * totalArea).ToArray();

            double x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
            int start = 0;

            while (start < items.Count)
            {
                double shortSide = Math.Min(w, h);
                bool isHorizontal = w >= h;

                int end = start;
                double rowArea = areas[start];
                double bestWorst = WorstAspectRatio(rowArea, shortSide, areas, start, end);

                while (end + 1 < items.Count)
                {
                    double newRowArea = rowArea + areas[end + 1];
                    double newWorst = WorstAspectRatio(newRowArea, shortSide, areas, start, end + 1);

                    if (newWorst <= bestWorst)
                    {
                        end++;
                        rowArea = newRowArea;
                        bestWorst = newWorst;
                    }
                    else
                    {
                        break;
                    }
                }

                double rowThickness = rowArea / shortSide;
                double offset = 0;
                for (int i = start; i <= end; i++)
                {
                    double itemLength = areas[i] / rowThickness;

                    if (isHorizontal)
                    {
                        result[i] = new Rect(x, y + offset, rowThickness, itemLength);
                    }
                    else
                    {
                        result[i] = new Rect(x + offset, y, itemLength, rowThickness);
                    }
                    offset += itemLength;
                }

                if (isHorizontal)
                {
                    x += rowThickness;
                    w -= rowThickness;
                }
                else
                {
                    y += rowThickness;
                    h -= rowThickness;
                }

                start = end + 1;
            }

            return result.ToList();
        }

        private static double WorstAspectRatio(double totalRowArea, double shortSide, double[] areas, int start, int end)
        {
            if (shortSide <= 0) return double.MaxValue;
            double rowThickness = totalRowArea / shortSide;
            if (rowThickness <= 0) return double.MaxValue;

            double worst = 0;
            for (int i = start; i <= end; i++)
            {
                double itemLength = areas[i] / rowThickness;
                if (itemLength <= 0) continue;
                double ratio = Math.Max(rowThickness / itemLength, itemLength / rowThickness);
                worst = Math.Max(worst, ratio);
            }
            return worst;
        }

        private static List<FileSystemItem> GetFilteredChildren(FileSystemItem parent, long minSizeBytes, string searchFilter)
        {
            var children = parent.Children
                .Where(c => c.Size >= minSizeBytes)
                .OrderByDescending(c => c.Size)
                .ToList();

            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                children = children.Where(c => ContainsNameRecursive(c, searchFilter)).ToList();
            }

            return children;
        }

        private static bool ContainsNameRecursive(FileSystemItem item, string filter)
        {
            if (item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
            if (item.IsDirectory)
                return item.Children.Any(c => ContainsNameRecursive(c, filter));
            return false;
        }

        private static Color DarkenColor(Color c, double factor)
        {
            return Color.FromRgb(
                ClampByte((int)(c.R * (1 - factor))),
                ClampByte((int)(c.G * (1 - factor))),
                ClampByte((int)(c.B * (1 - factor))));
        }

        private static byte ClampByte(int value) => (byte)Math.Max(0, Math.Min(255, value));
    }
}
