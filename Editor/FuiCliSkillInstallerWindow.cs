using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FUI.Cli
{
    /// <summary>
    /// 把 FUI CLI 自带 skill 安装到当前项目的 OpenCode 或 Codex skill 目录。
    /// </summary>
    public sealed class FuiCliSkillInstallerWindow : EditorWindow
    {
        const string MenuPath = "FUI/Install Skill";

        string statusMessage = string.Empty;
        MessageType statusType = MessageType.None;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            var window = GetWindow<FuiCliSkillInstallerWindow>();
            window.titleContent = new GUIContent("FUI Skill Installer");
            window.minSize = new Vector2(480f, 220f);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("FUI Skill Installer", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.HelpBox("把包内 Skill 复制到当前项目根目录，方便 AI 代理工具直接使用。", MessageType.Info);

            using (new EditorGUI.DisabledScope(false))
            {
                EditorGUILayout.LabelField("来源", FuiCliSkillInstaller.GetPackageSkillsRoot());
            }

            EditorGUILayout.Space(8f);

            // OpenCode 安装
            EditorGUILayout.LabelField("OpenCode", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(false))
            {
                EditorGUILayout.LabelField("目标", FuiCliSkillInstaller.GetOpenCodeSkillsRoot());
            }

            if (GUILayout.Button("Install to OpenCode", GUILayout.Height(28f)))
            {
                if (FuiCliSkillInstaller.TryInstallSkills(out var result))
                {
                    statusType = MessageType.Info;
                    statusMessage = $"[OpenCode] 已复制 {result.copiedFileCount} 个文件到 {result.destinationPath}";
                }
                else
                {
                    statusType = MessageType.Error;
                    statusMessage = result.errorMessage;
                }
            }

            EditorGUILayout.Space(8f);

            // Codex 安装
            EditorGUILayout.LabelField("Codex", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(false))
            {
                EditorGUILayout.LabelField("目标", FuiCliSkillInstaller.GetCodexSkillsRoot());
            }

            if (GUILayout.Button("Install to Codex", GUILayout.Height(28f)))
            {
                if (FuiCliSkillInstaller.TryInstallCodexSkills(out var result))
                {
                    statusType = MessageType.Info;
                    statusMessage = $"[Codex] 已复制 {result.copiedFileCount} 个文件到 {result.destinationPath}";
                }
                else
                {
                    statusType = MessageType.Error;
                    statusMessage = result.errorMessage;
                }
            }

            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }
        }
    }

    public static class FuiCliSkillInstaller
    {
        const string PackageName = "com.fujisheng.fui.cli";

        static string cachedPackageSkillsRoot;

        /// <summary>
        /// 通过 PackageInfo 解析包的实际磁盘路径，兼容本地/Git URL 等安装方式。
        /// </summary>
        public static string GetPackageSkillsRoot()
        {
            if (cachedPackageSkillsRoot != null)
            {
                return cachedPackageSkillsRoot;
            }

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FuiCliSkillInstaller).Assembly);
            if (packageInfo != null)
            {
                cachedPackageSkillsRoot = NormalizePath(Path.Combine(packageInfo.resolvedPath, "Skills"));
                return cachedPackageSkillsRoot;
            }

            // 兜底：直接拼 Packages 路径（仅本地嵌入包有效）
            cachedPackageSkillsRoot = NormalizePath(Path.Combine(GetProjectRoot(), "Packages", PackageName, "Skills"));
            return cachedPackageSkillsRoot;
        }

        /// <summary>OpenCode 项目级 skill 目标路径。</summary>
        public static string GetOpenCodeSkillsRoot()
        {
            return NormalizePath(Path.Combine(GetProjectRoot(), ".opencode", "skills"));
        }

        /// <summary>Codex 项目级 skill 目标路径。</summary>
        public static string GetCodexSkillsRoot()
        {
            return NormalizePath(Path.Combine(GetProjectRoot(), ".agents", "skills"));
        }

        [Obsolete("Use GetOpenCodeSkillsRoot() instead.")]
        public static string GetProjectSkillsRoot() => GetOpenCodeSkillsRoot();

        public static bool TryInstallSkills(out SkillInstallResult result)
        {
            return TryCopySkills(GetPackageSkillsRoot(), GetOpenCodeSkillsRoot(), out result);
        }

        public static bool TryInstallCodexSkills(out SkillInstallResult result)
        {
            return TryCopySkills(GetPackageSkillsRoot(), GetCodexSkillsRoot(), out result);
        }

        public static bool TryCopySkills(string sourceRoot, string destinationRoot, out SkillInstallResult result)
        {
            result = new SkillInstallResult
            {
                sourcePath = NormalizePath(sourceRoot),
                destinationPath = NormalizePath(destinationRoot)
            };

            if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            {
                result.errorMessage = "Skills 来源目录不存在。";
                return false;
            }

            try
            {
                EnsureDirectory(destinationRoot);
                var copiedFileCount = 0;
                CopyDirectoryRecursive(sourceRoot, destinationRoot, ref copiedFileCount);
                AssetDatabase.Refresh();

                result.copiedFileCount = copiedFileCount;
                return true;
            }
            catch (Exception exception)
            {
                result.errorMessage = exception.Message;
                return false;
            }
        }

        static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory, ref int copiedFileCount)
        {
            var files = Directory.GetFiles(sourceDirectory);
            var childDirectories = Directory.GetDirectories(sourceDirectory);
            var hasCopyableFile = false;
            foreach (var filePath in files)
            {
                if (!ShouldSkip(filePath))
                {
                    hasCopyableFile = true;
                    break;
                }
            }

            var hasCopyableChildDirectory = false;
            foreach (var childDirectory in childDirectories)
            {
                if (!ShouldSkip(childDirectory) && DirectoryHasCopyableContent(childDirectory))
                {
                    hasCopyableChildDirectory = true;
                    break;
                }
            }

            if (!hasCopyableFile && !hasCopyableChildDirectory)
            {
                return;
            }

            EnsureDirectory(destinationDirectory);

            foreach (var filePath in files)
            {
                if (ShouldSkip(filePath))
                {
                    continue;
                }

                var fileName = Path.GetFileName(filePath);
                var destinationPath = Path.Combine(destinationDirectory, fileName);
                File.Copy(filePath, destinationPath, true);
                copiedFileCount++;
            }

            foreach (var childDirectory in childDirectories)
            {
                if (ShouldSkip(childDirectory))
                {
                    continue;
                }

                if (!DirectoryHasCopyableContent(childDirectory))
                {
                    continue;
                }

                var directoryName = Path.GetFileName(childDirectory);
                var childDestination = Path.Combine(destinationDirectory, directoryName);
                CopyDirectoryRecursive(childDirectory, childDestination, ref copiedFileCount);
            }
        }

        static bool ShouldSkip(string path)
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return true;
            }

            if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        static bool DirectoryHasCopyableContent(string directoryPath)
        {
            foreach (var filePath in Directory.GetFiles(directoryPath))
            {
                if (!ShouldSkip(filePath))
                {
                    return true;
                }
            }

            foreach (var childDirectory in Directory.GetDirectories(directoryPath))
            {
                if (ShouldSkip(childDirectory))
                {
                    continue;
                }

                if (DirectoryHasCopyableContent(childDirectory))
                {
                    return true;
                }
            }

            return false;
        }

        static void EnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("目标目录不能为空。");
            }

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        static string GetProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
        }

        static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        public struct SkillInstallResult
        {
            public string sourcePath;
            public string destinationPath;
            public int copiedFileCount;
            public string errorMessage;
        }
    }
}
