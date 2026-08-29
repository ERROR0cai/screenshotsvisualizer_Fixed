using CommonPluginsStores;
using CommonPluginsShared.IO;
using Playnite.SDK;
using Playnite.SDK.Models;
using ScreenshotsVisualizer.Models;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace ScreenshotsVisualizer.Services
{
    /// <summary>
    /// Resolves folder paths and file patterns for screenshot sources.
    /// This service centralizes the same expansion logic used by scan and preview flows.
    /// </summary>
    public class SsvPathResolver
    {
        private static readonly ILogger _logger = LogManager.GetLogger();

        /// <summary>
        /// 清理文件名中的非法字符（与 GameSnap 保持一致）
        /// 注意：保留空格，不压缩多个空格
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unnamed";

            var invalid = Path.GetInvalidFileNameChars();
            var result = string.Concat(name.Split(invalid)).Trim();

            return string.IsNullOrWhiteSpace(result) ? "Unnamed" : result;
        }

        /// <summary>
        /// 自定义路径清理：只移除非法字符，不压缩空格
        /// </summary>
        private static string GetSafePathPreserveSpaces(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            try
            {
                // 只做基本的路径规范化，不压缩空格
                // 移除路径中的非法字符（但保留空格）
                char[] invalidPathChars = Path.GetInvalidPathChars();
                foreach (char c in invalidPathChars)
                {
                    path = path.Replace(c.ToString(), "");
                }

                // 获取完整路径（标准化斜杠等），但不会压缩空格
                return Path.GetFullPath(path);
            }
            catch
            {
                // 如果路径无效，返回原样
                return path;
            }
        }

        /// <summary>
        /// Resolves the configured screenshot folder path for a game.
        /// </summary>
        public string ResolvePath(Game game, FolderSettings folderSettings)
        {
            if (game == null || folderSettings == null || string.IsNullOrEmpty(folderSettings.ScreenshotsFolder))
            {
                return string.Empty;
            }

            string originalPath = folderSettings.ScreenshotsFolder;
            string originalGameName = game.Name;
            string sanitizedGameName = SanitizeFileName(originalGameName);

            _logger.Debug($"[SsvPathResolver] ResolvePath START:");
            _logger.Debug($"  ├─ Original game name: '{originalGameName}'");
            _logger.Debug($"  ├─ Sanitized game name: '{sanitizedGameName}'");
            _logger.Debug($"  ├─ Original folder pattern: '{originalPath}'");

            // 先手动替换 {Name} 为清理后的名称
            string pathAfterNameReplace = originalPath.Replace("{Name}", sanitizedGameName);
            _logger.Debug($"  ├─ After {{Name}} replace: '{pathAfterNameReplace}'");

            // 然后让 PlayniteTools 展开其他变量
            string expandedPath = PlayniteTools.StringExpandWithStores(game, pathAfterNameReplace);
            _logger.Debug($"  ├─ After StringExpandWithStores: '{expandedPath}'");

            // 使用自定义路径清理（保留空格，不压缩）
            string safePath = GetSafePathPreserveSpaces(expandedPath);
            _logger.Debug($"  └─ Final safe path: '{safePath}'");

            return safePath;
        }

        /// <summary>
        /// Builds the regex pattern used to match screenshot file names for a game.
        /// </summary>
        public string ResolveFilePatternRegex(Game game, FolderSettings folderSettings)
        {
            if (game == null || folderSettings == null || !folderSettings.UsedFilePattern || string.IsNullOrEmpty(folderSettings.FilePattern))
            {
                return string.Empty;
            }

            string originalPattern = folderSettings.FilePattern;
            string originalGameName = game.Name;
            string sanitizedGameName = SanitizeFileName(originalGameName);

            _logger.Debug($"[SsvPathResolver] ResolveFilePatternRegex START:");
            _logger.Debug($"  ├─ Original game name: '{originalGameName}'");
            _logger.Debug($"  ├─ Sanitized game name: '{sanitizedGameName}'");
            _logger.Debug($"  ├─ Original file pattern: '{originalPattern}'");

            // 先替换 {Name} 为清理后的名称
            string patternAfterNameReplace = originalPattern.Replace("{Name}", sanitizedGameName);
            _logger.Debug($"  ├─ After {{Name}} replace: '{patternAfterNameReplace}'");

            // 然后让 PlayniteTools 展开其他变量
            string expandedPattern = PlayniteTools.StringExpandWithStores(game, patternAfterNameReplace);
            _logger.Debug($"  ├─ After StringExpandWithStores: '{expandedPattern}'");

            string escapedPattern = EscapeRegexSpecialChars(expandedPattern);
            _logger.Debug($"  ├─ After EscapeRegexSpecialChars: '{escapedPattern}'");

            string patternWithWildcards = escapedPattern
                .Replace("\\*", ".*")
                .Replace("\\{digit\\}", @"\d+")
                .Replace("\\{DateModified\\}", @"[0-9]{4}[-_][0-9]{2}[-_][0-9]{2}")
                .Replace("\\{DateTimeModified\\}", @"[0-9]{4}[-_][0-9]{2}[-_][0-9]{2}[ -_][0-9]{2}[-_][0-9]{2}[-_][0-9]{2}");
            _logger.Debug($"  ├─ After wildcard replacements: '{patternWithWildcards}'");

            // 构建游戏名正则（空格允许任意数量，以匹配可能的压缩）
            string gameNameForRegex = sanitizedGameName;
            // 将每个空格替换为 [ ]*（匹配任意数量的空格）
            string safeGameNamePattern = Regex.Escape(gameNameForRegex).Replace("\\ ", "[ ]*");
            _logger.Debug($"  ├─ Game name regex: '{safeGameNamePattern}' (from '{gameNameForRegex}')");

            string finalPattern = patternWithWildcards.Replace(Regex.Escape(gameNameForRegex), safeGameNamePattern);
            _logger.Debug($"  ├─ After game name regex replace: '{finalPattern}'");

            string anchoredPattern = "^" + finalPattern + "$";
            _logger.Debug($"  └─ Final anchored regex: '{anchoredPattern}'");

            return anchoredPattern;
        }

        private static string EscapeRegexSpecialChars(string input)
        {
            string specialChars = @".^$*+?(){}[]|\";
            StringBuilder escapedString = new StringBuilder();

            foreach (char c in input)
            {
                if (specialChars.IndexOf(c) >= 0)
                {
                    _ = escapedString.Append('\\');
                }

                _ = escapedString.Append(c);
            }

            return escapedString.ToString();
        }
    }
}