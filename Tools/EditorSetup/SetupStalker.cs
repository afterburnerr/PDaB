using System;
using System.IO;
using System.Linq;
using FPLab.Gameplay.Enemies;
using FPLab.Gameplay.Maze;
using FPLab.Gameplay.Session;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupStalker
{
    private const string Art = "Assets/Characters/Stalker/";
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before authoring the stalker.");
        AssetDatabase.Refresh();
        Directory.CreateDirectory(Art + "Materials");
        Directory.CreateDirectory(Art + "Animation");
        Directory.CreateDirectory(Art + "Prefabs");
        AssetDatabase.Refresh();
        string modelPath = Art + "Model/Stalker.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.importCameras = importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.isReadable = false;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            int prefix = clip.name.LastIndexOf("Stalker_", StringComparison.Ordinal);
            clip.name = prefix >= 0 ? clip.name.Substring(prefix + 8) : clip.name;
            clip.loopTime = clip.name == "Idle" || clip.name == "Walk" || clip.name == "Run";
            clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        var animations = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__")).ToDictionary(c => c.name);
        foreach (string name in new[] {"Idle", "Walk", "Run", "Recoil", "Attack"})
            if (!animations.ContainsKey(name)) throw new InvalidOperationException("Missing animation: " + name);
        string texturePath = Art + "Textures/StalkerDiffuse.png";
        var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        textureImporter.textureType = TextureImporterType.Default;
        textureImporter.sRGBTexture = true;
        textureImporter.mipmapEnabled = true;
        textureImporter.maxTextureSize = 2048;
        textureImporter.filterMode = FilterMode.Trilinear;
        textureImporter.anisoLevel = 4;
        textureImporter.textureCompression = TextureImporterCompression.CompressedHQ;
        textureImporter.SaveAndReimport();
        string materialPath = Art + "Materials/StalkerSkin.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.12f);
        material.DisableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(material);
        AnimatorController controller = MakeController(animations);
        var template = new GameObject("Entrance Stalker");
        template.AddComponent<CharacterController>();
        EntranceStalker logic = template.AddComponent<EntranceStalker>();
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
        visual.transform.SetParent(template.transform, false);
        visual.name = "Stalker Visual";
        Animator animator = visual.GetComponent<Animator>();
        if (animator == null) animator = visual.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (SkinnedMeshRenderer renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            renderer.sharedMaterial = material;
            renderer.updateWhenOffscreen = true;
        }
        Assign(logic, "visualRoot", visual, "animator", animator);
        var balance = new SerializedObject(logic);
        balance.FindProperty("graceSeconds").floatValue = 18f;
        balance.FindProperty("minimumSpawnCells").intValue = 12;
        balance.FindProperty("movementSpeed").floatValue = 3.5f;
        balance.ApplyModifiedPropertiesWithoutUndo();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(template, Art + "Prefabs/EntranceStalker.prefab");
        UnityEngine.Object.DestroyImmediate(template);
        var old = UnityEngine.Object.FindAnyObjectByType<EntranceStalker>(FindObjectsInactive.Include);
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        GameObject enemy = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        logic = enemy.GetComponent<EntranceStalker>();
        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        EscapeSession session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        var mazeFields = new SerializedObject(maze);
        Transform player = (Transform)mazeFields.FindProperty("player").objectReferenceValue;
        Assign(logic, "maze", maze, "session", session, "player", player);
        PlayerCheats cheats = session.GetComponent<PlayerCheats>();
        if (cheats == null) cheats = session.gameObject.AddComponent<PlayerCheats>();
        Assign(cheats, "session", session, "maze", maze, "stalker", logic, "player", player);
        Camera camera = player.GetComponentInChildren<Camera>();
        foreach (MonoBehaviour component in camera.GetComponents<MonoBehaviour>())
            if (component != null && component.GetType().Name == "StalkerCaptureView")
                UnityEngine.Object.DestroyImmediate(component);
        Transform oldCaptureLight = camera.transform.Find("Capture light");
        if (oldCaptureLight != null) UnityEngine.Object.DestroyImmediate(oldCaptureLight.gameObject);
        var sessionFields = new SerializedObject(session);
        sessionFields.FindProperty("captureDuration").floatValue = 1.15f;
        sessionFields.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(maze.gameObject.scene);
        EditorSceneManager.SaveScene(maze.gameObject.scene);
        AssetDatabase.SaveAssets();
        return new {saved=true, animations=animations.Keys.ToArray(), material=material.name, enemy=enemy.name, cheats=true};
    }

    private static AnimatorController MakeController(System.Collections.Generic.Dictionary<string,AnimationClip> clips)
    {
        string path = Art + "Animation/Stalker.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (var state in machine.states) machine.RemoveState(state.state);
        foreach (var transition in machine.anyStateTransitions) machine.RemoveAnyStateTransition(transition);
        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Recoil", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        var blend = new BlendTree { name="Locomotion", blendParameter="Speed", useAutomaticThresholds=false };
        AssetDatabase.AddObjectToAsset(blend, controller);
        blend.AddChild(clips["Idle"],0f); blend.AddChild(clips["Walk"],1.8f); blend.AddChild(clips["Run"],4.1f);
        AnimatorState moving = machine.AddState("Locomotion"); moving.motion=blend; machine.defaultState=moving;
        AnimatorState recoil = machine.AddState("Recoil"); recoil.motion=clips["Recoil"];
        AnimatorState attack = machine.AddState("Attack"); attack.motion=clips["Attack"];
        foreach (var pair in new[] {new {Name="Recoil",State=recoil},new {Name="Attack",State=attack}})
        {
            var transition=machine.AddAnyStateTransition(pair.State);
            transition.hasExitTime=false;transition.duration=0.06f;transition.canTransitionToSelf=false;
            transition.AddCondition(AnimatorConditionMode.If,0,pair.Name);
        }
        var recover=recoil.AddTransition(moving);recover.hasExitTime=true;recover.exitTime=0.95f;recover.duration=0.15f;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void Assign(UnityEngine.Object target, params object[] values)
    {
        var fields = new SerializedObject(target);
        for(int i=0;i<values.Length;i+=2) fields.FindProperty((string)values[i]).objectReferenceValue=values[i+1] as UnityEngine.Object;
        fields.ApplyModifiedPropertiesWithoutUndo();
    }
}
