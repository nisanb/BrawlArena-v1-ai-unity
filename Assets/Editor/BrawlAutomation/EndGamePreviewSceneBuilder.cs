using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Creates the isolated end-game UI preview without opening or replacing
    /// the user's current editor scene.
    /// </summary>
    public static class EndGamePreviewSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/EndGamePreview.unity";

        [MenuItem("Brawl Arena/Build End Game Preview")]
        static void BuildFromMenu()
        {
            Debug.Log(BuildPreviewScene());
        }

        public static string BuildPreviewScene()
        {
            var originalActiveScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);

                var cameraRoot = new GameObject("Preview Camera");
                SceneManager.MoveGameObjectToScene(cameraRoot, scene);
                var camera = cameraRoot.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.06f, 0.12f);
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                cameraRoot.transform.position = new Vector3(0f, 0f, -10f);

                var theme = ThemeKit.CreateThemeObject();
                SceneManager.MoveGameObjectToScene(theme.gameObject, scene);

                var hudRoot = new GameObject("HUD");
                SceneManager.MoveGameObjectToScene(hudRoot, scene);
                hudRoot.AddComponent<BrawlHUD>();

                var previewRoot = new GameObject("EndGamePreview");
                SceneManager.MoveGameObjectToScene(previewRoot, scene);
                var preview = previewRoot.AddComponent<EndGamePreviewController>();
                preview.winningTeam = TeamId.Blue;
                preview.eliminations = 3;
                preview.brawlerPoints = 54;
                preview.coins = 40;
                preview.brawlerLevel = 1;
                preview.pointsBefore = 18;
                preview.pointsAfter = 72;
                preview.pointsNeeded = Progress.PointsNeeded(preview.brawlerLevel);

                Directory.CreateDirectory("Assets/Scenes");
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                return "Created " + ScenePath + " without changing the active scene.";
            }
            finally
            {
                if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                    SceneManager.SetActiveScene(originalActiveScene);
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
