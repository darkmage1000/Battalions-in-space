using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BIS.EditorTools
{
    /// <summary>
    /// One-time project setup (runs on load if anything is missing) and the build menu.
    /// Battalions > Setup Project / Build Windows / Build Android.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(ScenePath) || !SessionState.GetBool("bis_setup", false))
                {
                    SessionState.SetBool("bis_setup", true);
                    Setup(false);
                }
            };
        }

        [MenuItem("Battalions/Setup Project")]
        public static void SetupMenu() => Setup(true);

        public static void Setup(bool verbose)
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "Battalions Studio";
            PlayerSettings.productName = "Battalions in Space";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.battalions.space");
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.battalions.space");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            AddPadAxes();
            AssetDatabase.SaveAssets();
            if (verbose) Debug.Log("[Battalions] project setup done");
        }

        static void AddPadAxes()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var axes = so.FindProperty("m_Axes");
            (string name, int axis)[] want = { ("PadLX", 0), ("PadLY", 1), ("PadRX", 3), ("PadRY", 4), ("PadDX", 5), ("PadDY", 6), ("PadLT", 8), ("PadRT", 9) };
            foreach (var (name, axis) in want)
            {
                bool found = false;
                for (int i = 0; i < axes.arraySize; i++)
                    if (axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name").stringValue == name) { found = true; break; }
                if (found) continue;
                axes.arraySize++;
                var p = axes.GetArrayElementAtIndex(axes.arraySize - 1);
                p.FindPropertyRelative("m_Name").stringValue = name;
                p.FindPropertyRelative("descriptiveName").stringValue = name;
                p.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
                p.FindPropertyRelative("negativeButton").stringValue = "";
                p.FindPropertyRelative("positiveButton").stringValue = "";
                p.FindPropertyRelative("altNegativeButton").stringValue = "";
                p.FindPropertyRelative("altPositiveButton").stringValue = "";
                p.FindPropertyRelative("gravity").floatValue = 0;
                p.FindPropertyRelative("dead").floatValue = 0.05f;
                p.FindPropertyRelative("sensitivity").floatValue = 1;
                p.FindPropertyRelative("snap").boolValue = false;
                p.FindPropertyRelative("invert").boolValue = false;
                p.FindPropertyRelative("type").intValue = 2;
                p.FindPropertyRelative("axis").intValue = axis;
                p.FindPropertyRelative("joyNum").intValue = 0;
            }
            so.ApplyModifiedProperties();
        }

        [MenuItem("Battalions/Build Windows")]
        public static void BuildWindows()
        {
            Setup(false);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/BattalionsInSpace.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var rep = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Battalions] Windows build: {rep.summary.result}, {rep.summary.totalSize / (1024 * 1024)} MB, {rep.summary.totalErrors} errors -> {opts.locationPathName}");
        }

        [MenuItem("Battalions/Build Android")]
        public static void BuildAndroid()
        {
            Setup(false);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Android/BattalionsInSpace.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var rep = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Battalions] Android build: {rep.summary.result}, {rep.summary.totalErrors} errors -> {opts.locationPathName}");
        }
    }
}
