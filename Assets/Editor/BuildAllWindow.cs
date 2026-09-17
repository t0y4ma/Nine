using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BuildAllWindow : EditorWindow
{
    // ============================================================
    // Window
    // ============================================================

    [MenuItem("Build/Build Settings")]
    public static void ShowWindow()
    {
        var window = GetWindow<BuildAllWindow>("Build Settings");

        window.minSize = new Vector2(750, 700);
        window.Show();
    }


    // ============================================================
    // Settings
    // ============================================================

    private string webGLBuildPath = "Build/WebGL";

    private string linuxServerBuildPath = "Build/LinuxServer";

    private string zipPath = "Build/LinuxServer.zip";

    private string linuxServerExecutableName = "NineServer";

    private Vector2 scrollPosition;

    private readonly List<string> excludePaths =
        new List<string>();


    // ============================================================
    // EditorPrefs
    // ============================================================

    private const string PrefPrefix =
        "BuildAllWindow_";


    // ============================================================
    // Unity
    // ============================================================

    private void OnEnable()
    {
        LoadSettings();
    }


    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(
            scrollPosition);

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField(
            "Build Settings",
            EditorStyles.boldLabel);

        EditorGUILayout.Space(10);


        // ========================================================
        // Build Paths
        // ========================================================

        EditorGUILayout.LabelField(
            "Build Paths",
            EditorStyles.boldLabel);

        EditorGUILayout.Space(5);

        DrawPathField(
            "WebGL Build",
            ref webGLBuildPath,
            "Select WebGL build directory.");

        DrawPathField(
            "Linux Server Build",
            ref linuxServerBuildPath,
            "Select Linux Server build directory.");

        DrawPathField(
            "ZIP Output",
            ref zipPath,
            "Select ZIP output path.",
            true);


        EditorGUILayout.Space(15);


        // ========================================================
        // Linux Server
        // ========================================================

        EditorGUILayout.LabelField(
            "Linux Server",
            EditorStyles.boldLabel);

        EditorGUILayout.Space(5);

        linuxServerExecutableName =
            EditorGUILayout.TextField(
                "Executable Name",
                linuxServerExecutableName);


        EditorGUILayout.HelpBox(
            "The Linux Server executable will be created as:\n\n" +
            Path.Combine(
                linuxServerBuildPath,
                linuxServerExecutableName + ".x86_64")
                .Replace('\\', '/'),
            MessageType.Info);


        EditorGUILayout.Space(15);


        // ========================================================
        // Exclusions
        // ========================================================

        EditorGUILayout.LabelField(
            "ZIP Exclusions",
            EditorStyles.boldLabel);

        EditorGUILayout.Space(5);

        EditorGUILayout.HelpBox(
            "Exclude files or folders from the ZIP.\n\n" +
            "You can either type the path manually or use the " +
            "Folder / File buttons.\n\n" +
            "Paths are relative to the Linux Server Build path.",
            MessageType.Info);

        EditorGUILayout.Space(5);

        DrawExcludeList();


        EditorGUILayout.Space(15);


        // ========================================================
        // Save
        // ========================================================

        if (GUILayout.Button(
                "Save Settings",
                GUILayout.Height(30)))
        {
            SaveSettings();

            EditorUtility.DisplayDialog(
                "Build Settings",
                "Settings saved.",
                "OK");
        }


        EditorGUILayout.Space(10);


        // ========================================================
        // Build
        // ========================================================

        Color oldColor =
            GUI.backgroundColor;

        GUI.backgroundColor =
            new Color(0.3f, 0.8f, 0.3f);

        if (GUILayout.Button(
                "BUILD ALL",
                GUILayout.Height(50)))
        {
            SaveSettings();

            BuildAll();
        }

        GUI.backgroundColor =
            oldColor;


        EditorGUILayout.Space(15);


        // ========================================================
        // Build Flow
        // ========================================================

        EditorGUILayout.LabelField(
            "Build Process",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "1. WebGL Build\n" +
            "2. Linux Server Build\n" +
            "3. Switch Active Build Target to WebGL\n" +
            "4. Create ZIP\n" +
            "5. Apply exclusion settings",
            MessageType.None);


        EditorGUILayout.EndScrollView();
    }


    // ============================================================
    // Path Field
    // ============================================================

    private void DrawPathField(
        string label,
        ref string path,
        string dialogTitle,
        bool file = false)
    {
        EditorGUILayout.BeginHorizontal();

        path = EditorGUILayout.TextField(
            label,
            path);

        if (GUILayout.Button(
                "...",
                GUILayout.Width(30)))
        {
            if (file)
            {
                string fullPath =
                    Path.GetFullPath(path);

                string directory =
                    Path.GetDirectoryName(fullPath);

                if (string.IsNullOrEmpty(directory))
                {
                    directory =
                        Directory.GetCurrentDirectory();
                }

                string fileName =
                    Path.GetFileName(path);

                string selected =
                    EditorUtility.SaveFilePanel(
                        dialogTitle,
                        directory,
                        fileName,
                        "zip");

                if (!string.IsNullOrEmpty(selected))
                {
                    path =
                        MakeProjectRelativePath(
                            selected);
                }
            }
            else
            {
                string selected =
                    EditorUtility.OpenFolderPanel(
                        dialogTitle,
                        Path.GetFullPath(path),
                        "");

                if (!string.IsNullOrEmpty(selected))
                {
                    path =
                        MakeProjectRelativePath(
                            selected);
                }
            }
        }

        EditorGUILayout.EndHorizontal();
    }


    // ============================================================
    // Exclusion List
    // ============================================================

    private void DrawExcludeList()
    {
        if (excludePaths.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No exclusions configured.",
                MessageType.Info);
        }


        for (int i = 0;
             i < excludePaths.Count;
             i++)
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox);


            // ----------------------------------------------------
            // Path text field
            // ----------------------------------------------------

            EditorGUILayout.BeginHorizontal();

            excludePaths[i] =
                EditorGUILayout.TextField(
                    $"Exclude {i + 1}",
                    excludePaths[i]);

            if (GUILayout.Button(
                    "X",
                    GUILayout.Width(25)))
            {
                excludePaths.RemoveAt(i);

                GUIUtility.ExitGUI();

                return;
            }

            EditorGUILayout.EndHorizontal();


            // ----------------------------------------------------
            // Selection buttons
            // ----------------------------------------------------

            EditorGUILayout.BeginHorizontal();

            GUILayout.FlexibleSpace();


            // Folder
            if (GUILayout.Button(
                    "Select Folder",
                    GUILayout.Width(120)))
            {
                SelectExcludeFolder(i);
            }


            // File
            if (GUILayout.Button(
                    "Select File",
                    GUILayout.Width(120)))
            {
                SelectExcludeFile(i);
            }


            EditorGUILayout.EndHorizontal();


            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(3);
        }


        // --------------------------------------------------------
        // Add
        // --------------------------------------------------------

        if (GUILayout.Button(
                "+ Add Exclusion",
                GUILayout.Height(28)))
        {
            excludePaths.Add("");
        }
    }


    // ============================================================
    // Select Exclude Folder
    // ============================================================

    private void SelectExcludeFolder(
        int index)
    {
        string linuxBuildFullPath =
            Path.GetFullPath(
                linuxServerBuildPath);


        if (!Directory.Exists(
                linuxBuildFullPath))
        {
            EditorUtility.DisplayDialog(
                "Linux Server Build Path",
                "The Linux Server Build directory does not exist yet.\n\n" +
                "Please set the path first.\n\n" +
                linuxBuildFullPath,
                "OK");

            return;
        }


        string selected =
            EditorUtility.OpenFolderPanel(
                "Select Exclude Folder",
                linuxBuildFullPath,
                "");


        if (string.IsNullOrEmpty(selected))
        {
            return;
        }


        if (!IsPathInside(
                selected,
                linuxBuildFullPath))
        {
            EditorUtility.DisplayDialog(
                "Invalid Folder",
                "Please select a folder inside the Linux Server Build directory.",
                "OK");

            return;
        }


        string relativePath =
            Path.GetRelativePath(
                linuxBuildFullPath,
                selected);


        if (relativePath == ".")
        {
            EditorUtility.DisplayDialog(
                "Invalid Folder",
                "The Linux Server Build root itself cannot be excluded.",
                "OK");

            return;
        }


        excludePaths[index] =
            NormalizePath(relativePath);
    }


    // ============================================================
    // Select Exclude File
    // ============================================================

    private void SelectExcludeFile(
        int index)
    {
        string linuxBuildFullPath =
            Path.GetFullPath(
                linuxServerBuildPath);


        if (!Directory.Exists(
                linuxBuildFullPath))
        {
            EditorUtility.DisplayDialog(
                "Linux Server Build Path",
                "The Linux Server Build directory does not exist yet.\n\n" +
                "Please set the path first.\n\n" +
                linuxBuildFullPath,
                "OK");

            return;
        }


        string selected =
            EditorUtility.OpenFilePanel(
                "Select Exclude File",
                linuxBuildFullPath,
                "");


        if (string.IsNullOrEmpty(selected))
        {
            return;
        }


        if (!IsPathInside(
                selected,
                linuxBuildFullPath))
        {
            EditorUtility.DisplayDialog(
                "Invalid File",
                "Please select a file inside the Linux Server Build directory.",
                "OK");

            return;
        }


        string relativePath =
            Path.GetRelativePath(
                linuxBuildFullPath,
                selected);


        excludePaths[index] =
            NormalizePath(relativePath);
    }


    // ============================================================
    // Build All
    // ============================================================

    private void BuildAll()
    {
        try
        {
            if (!ValidateSettings())
            {
                return;
            }


            Debug.Log(
                "========================================");

            Debug.Log(
                "Build All Start");

            Debug.Log(
                "========================================");


            // ====================================================
            // WebGL
            // ====================================================

            EditorUtility.DisplayProgressBar(
                "Build All",
                "Building WebGL...",
                0.1f);


            if (!BuildWebGL())
            {
                throw new Exception(
                    "WebGL build failed.");
            }


            // ====================================================
            // Linux Server
            // ====================================================

            EditorUtility.DisplayProgressBar(
                "Build All",
                "Building Linux Server...",
                0.4f);


            if (!BuildLinuxServer())
            {
                throw new Exception(
                    "Linux Server build failed.");
            }


            // ====================================================
            // Switch to WebGL
            // ====================================================

            EditorUtility.DisplayProgressBar(
                "Build All",
                "Switching Build Target to WebGL...",
                0.7f);


            if (!SwitchToWebGL())
            {
                throw new Exception(
                    "Failed to switch Build Target to WebGL.");
            }


            // ====================================================
            // ZIP
            // ====================================================

            EditorUtility.DisplayProgressBar(
                "Build All",
                "Creating ZIP...",
                0.8f);


            CreateZip();


            // ====================================================
            // Complete
            // ====================================================

            EditorUtility.DisplayProgressBar(
                "Build All",
                "Complete",
                1.0f);


            string fullZipPath =
                Path.GetFullPath(zipPath);


            Debug.Log(
                "========================================");

            Debug.Log(
                "Build All Complete");

            Debug.Log(
                "ZIP: " + fullZipPath);

            Debug.Log(
                "========================================");


            EditorUtility.DisplayDialog(
                "Build Complete",
                "Build completed successfully.\n\n" +
                "ZIP:\n" +
                fullZipPath,
                "OK");
        }
        catch (Exception e)
        {
            Debug.LogException(e);


            EditorUtility.DisplayDialog(
                "Build Error",
                e.Message,
                "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }


    // ============================================================
    // WebGL
    // ============================================================

    private bool BuildWebGL()
    {
        PrepareDirectory(
            webGLBuildPath);


        string[] scenes =
            GetEnabledScenes();


        var options =
            new BuildPlayerOptions
            {
                scenes =
                    scenes,

                locationPathName =
                    webGLBuildPath,

                target =
                    BuildTarget.WebGL,

                options =
                    BuildOptions.None
            };


        BuildReport report =
            BuildPipeline.BuildPlayer(
                options);


        LogBuildResult(
            "WebGL",
            report);


        return report.summary.result ==
               BuildResult.Succeeded;
    }


    // ============================================================
    // Linux Server
    // ============================================================

    private bool BuildLinuxServer()
    {
        PrepareDirectory(
            linuxServerBuildPath);


        string[] scenes =
            GetEnabledScenes();


        string executableName =
            linuxServerExecutableName.Trim();


        if (executableName.EndsWith(
                ".x86_64",
                StringComparison.OrdinalIgnoreCase))
        {
            executableName =
                executableName.Substring(
                    0,
                    executableName.Length - 7);
        }


        string executablePath =
            Path.Combine(
                linuxServerBuildPath,
                executableName + ".x86_64");


        Debug.Log(
            "Linux Server executable:\n" +
            executablePath);


        var options =
            new BuildPlayerOptions
            {
                scenes =
                    scenes,

                locationPathName =
                    executablePath,

                target =
                    BuildTarget.StandaloneLinux64,

                subtarget =
                    (int)StandaloneBuildSubtarget.Server,

                options =
                    BuildOptions.None
            };


        BuildReport report =
            BuildPipeline.BuildPlayer(
                options);


        LogBuildResult(
            "Linux Server",
            report);


        return report.summary.result ==
               BuildResult.Succeeded;
    }


    // ============================================================
    // Switch WebGL
    // ============================================================

    private bool SwitchToWebGL()
    {
        return
            EditorUserBuildSettings
                .SwitchActiveBuildTarget(
                    BuildTargetGroup.WebGL,
                    BuildTarget.WebGL);
    }


    // ============================================================
    // ZIP
    // ============================================================

    private void CreateZip()
    {
        if (!Directory.Exists(
                linuxServerBuildPath))
        {
            throw new DirectoryNotFoundException(
                "Linux Server build directory not found:\n" +
                linuxServerBuildPath);
        }


        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }


        string zipDirectory =
            Path.GetDirectoryName(zipPath);


        if (!string.IsNullOrEmpty(
                zipDirectory))
        {
            Directory.CreateDirectory(
                zipDirectory);
        }


        using (ZipArchive archive =
               ZipFile.Open(
                   zipPath,
                   ZipArchiveMode.Create))
        {
            AddDirectoryToZip(
                archive,
                linuxServerBuildPath,
                linuxServerBuildPath);
        }
    }


    // ============================================================
    // Add Directory to ZIP
    // ============================================================

    private void AddDirectoryToZip(
        ZipArchive archive,
        string rootDirectory,
        string currentDirectory)
    {
        foreach (string file in
                 Directory.GetFiles(
                     currentDirectory))
        {
            string relativePath =
                Path.GetRelativePath(
                    rootDirectory,
                    file);


            if (IsExcluded(relativePath))
            {
                Debug.Log(
                    "Excluded file: " +
                    relativePath);

                continue;
            }


            archive.CreateEntryFromFile(
                file,
                NormalizePath(relativePath),
                System.IO.Compression
                    .CompressionLevel
                    .Optimal);
        }


        foreach (string directory in
                 Directory.GetDirectories(
                     currentDirectory))
        {
            string relativePath =
                Path.GetRelativePath(
                    rootDirectory,
                    directory);


            if (IsExcluded(relativePath))
            {
                Debug.Log(
                    "Excluded directory: " +
                    relativePath);

                continue;
            }


            AddDirectoryToZip(
                archive,
                rootDirectory,
                directory);
        }
    }


    // ============================================================
    // Exclusion Check
    // ============================================================

    private bool IsExcluded(
        string relativePath)
    {
        string normalizedPath =
            NormalizePath(relativePath);


        foreach (string exclude
                 in excludePaths)
        {
            if (string.IsNullOrWhiteSpace(
                    exclude))
            {
                continue;
            }


            string normalizedExclude =
                NormalizePath(exclude);


            // Exact file/folder
            if (string.Equals(
                    normalizedPath,
                    normalizedExclude,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }


            // Child of excluded folder
            if (normalizedPath.StartsWith(
                    normalizedExclude + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }


        return false;
    }


    // ============================================================
    // Validation
    // ============================================================

    private bool ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(
                webGLBuildPath))
        {
            ShowValidationError(
                "WebGL Build Path is empty.");

            return false;
        }


        if (string.IsNullOrWhiteSpace(
                linuxServerBuildPath))
        {
            ShowValidationError(
                "Linux Server Build Path is empty.");

            return false;
        }


        if (string.IsNullOrWhiteSpace(
                zipPath))
        {
            ShowValidationError(
                "ZIP Output Path is empty.");

            return false;
        }


        if (string.IsNullOrWhiteSpace(
                linuxServerExecutableName))
        {
            ShowValidationError(
                "Linux Server Executable Name is empty.");

            return false;
        }


        if (GetEnabledScenes().Length == 0)
        {
            ShowValidationError(
                "No enabled scenes exist in Build Settings.");

            return false;
        }


        return true;
    }


    private void ShowValidationError(
        string message)
    {
        EditorUtility.DisplayDialog(
            "Invalid Settings",
            message,
            "OK");
    }


    // ============================================================
    // Path Utilities
    // ============================================================

    private static bool IsPathInside(
        string path,
        string root)
    {
        string fullPath =
            Path.GetFullPath(path)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        string fullRoot =
            Path.GetFullPath(root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);


        return
            fullPath.Equals(
                fullRoot,
                StringComparison.OrdinalIgnoreCase)
            ||
            fullPath.StartsWith(
                fullRoot +
                Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            ||
            fullPath.StartsWith(
                fullRoot +
                Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }


    private static string NormalizePath(
        string path)
    {
        return path
            .Replace('\\', '/')
            .Trim('/');
    }


    private static string MakeProjectRelativePath(
        string path)
    {
        string projectPath =
            Directory.GetParent(
                Application.dataPath).FullName;


        string fullPath =
            Path.GetFullPath(path);


        if (fullPath.StartsWith(
                projectPath,
                StringComparison.OrdinalIgnoreCase))
        {
            fullPath =
                fullPath.Substring(
                    projectPath.Length)
                    .TrimStart(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
        }


        return NormalizePath(fullPath);
    }


    // ============================================================
    // Build Utilities
    // ============================================================

    private static string[] GetEnabledScenes()
    {
        return EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
    }


    private static void PrepareDirectory(
        string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(
                path,
                true);
        }


        Directory.CreateDirectory(path);
    }


    private static void LogBuildResult(
        string buildName,
        BuildReport report)
    {
        Debug.Log(
            $"===== {buildName} Build Result =====");

        Debug.Log(
            $"Result: {report.summary.result}");

        Debug.Log(
            $"Total Time: {report.summary.totalTime}");

        Debug.Log(
            $"Total Size: {report.summary.totalSize} bytes");

        Debug.Log(
            $"Total Errors: {report.summary.totalErrors}");

        Debug.Log(
            $"Total Warnings: {report.summary.totalWarnings}");


        foreach (BuildStep step in report.steps)
        {
            foreach (BuildStepMessage message
                     in step.messages)
            {
                if (message.type ==
                    LogType.Error ||
                    message.type ==
                    LogType.Exception)
                {
                    Debug.LogError(
                        "[BUILD ERROR] " +
                        message.content);
                }
                else if (message.type ==
                         LogType.Warning)
                {
                    Debug.LogWarning(
                        "[BUILD WARNING] " +
                        message.content);
                }
            }
        }
    }


    // ============================================================
    // EditorPrefs
    // ============================================================

    private void SaveSettings()
    {
        EditorPrefs.SetString(
            PrefPrefix + "WebGLBuildPath",
            webGLBuildPath);

        EditorPrefs.SetString(
            PrefPrefix + "LinuxServerBuildPath",
            linuxServerBuildPath);

        EditorPrefs.SetString(
            PrefPrefix + "ZipPath",
            zipPath);

        EditorPrefs.SetString(
            PrefPrefix +
            "LinuxServerExecutableName",
            linuxServerExecutableName);


        EditorPrefs.SetInt(
            PrefPrefix + "ExcludeCount",
            excludePaths.Count);


        for (int i = 0;
             i < excludePaths.Count;
             i++)
        {
            EditorPrefs.SetString(
                PrefPrefix +
                "Exclude_" +
                i,
                excludePaths[i]);
        }
    }


    private void LoadSettings()
    {
        webGLBuildPath =
            EditorPrefs.GetString(
                PrefPrefix + "WebGLBuildPath",
                "Build/WebGL");


        linuxServerBuildPath =
            EditorPrefs.GetString(
                PrefPrefix + "LinuxServerBuildPath",
                "Build/LinuxServer");


        zipPath =
            EditorPrefs.GetString(
                PrefPrefix + "ZipPath",
                "Build/LinuxServer.zip");


        linuxServerExecutableName =
            EditorPrefs.GetString(
                PrefPrefix +
                "LinuxServerExecutableName",
                "NineServer");


        excludePaths.Clear();


        int count =
            EditorPrefs.GetInt(
                PrefPrefix +
                "ExcludeCount",
                0);


        for (int i = 0;
             i < count;
             i++)
        {
            excludePaths.Add(
                EditorPrefs.GetString(
                    PrefPrefix +
                    "Exclude_" +
                    i,
                    ""));
        }
    }
}
