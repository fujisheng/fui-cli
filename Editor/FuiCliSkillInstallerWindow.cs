using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FUI.Cli
{
    /// <summary>
    /// 把 FUI CLI 自带 skill 安装到当前项目 .opencode/skills。
    /// </summary>
    public sealed class FuiCliSkillInstallerWindow : EditorWindow
    {
        const string MenuPath = "FUI/Install OpenCode Skill";

        string statusMessage = string.Empty;
        MessageType statusType = MessageType.None;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            var window = GetWindow<FuiCliSkillInstallerWindow>();
            window.titleContent = new GUIContent("FUI Skill");
            window.minSize = new Vector2(480f, 180f);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("FUI OpenCode Skill", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.HelpBox("把包内 Skill 复制到当前项目根目录 .opencode/skills，方便 OpenCode 直接使用。", MessageType.Info);

            using (new EditorGUI.DisabledScope(false))
            {
                EditorGUILayout.LabelField("来源", FuiCliSkillInstaller.GetPackageSkillsRoot());
                EditorGUILayout.LabelField("目标", FuiCliSkillInstaller.GetProjectSkillsRoot());
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Install OpenCode Skill", GUILayout.Height(28f)))
            {
                if (FuiCliSkillInstaller.TryInstallSkills(out var result))
                {
                    statusType = MessageType.Info;
                    statusMessage = $"已复制 {result.copiedFileCount} 个文件到 {result.destinationPath}";
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
        public static string GetPackageSkillsRoot()
        {
            return NormalizePath(Path.Combine(GetProjectRoot(), "Packages", "com.fujisheng.fui.cli", "Skills"));
        }

        public static string GetProjectSkillsRoot()
        {
            return NormalizePath(Path.Combine(GetProjectRoot(), ".opencode", "skills"));
        }

        public static bool TryInstallSkills(out SkillInstallResult result)
        {
            return TryCopySkills(GetPackageSkillsRoot(), GetProjectSkillsRoot(), out result);
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
