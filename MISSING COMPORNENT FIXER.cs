using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;

public class AnimationControllerPathFixerSafe : EditorWindow
{
    private GameObject rootObject;
    private AnimatorController animatorController;
    private Dictionary<string, string> remapLog = new Dictionary<string, string>();
    private Dictionary<string, string> warnLog = new Dictionary<string, string>();

    [MenuItem("Tools/Animation Controller Path Fixer Safe")]
    public static void ShowWindow()
    {
        GetWindow<AnimationControllerPathFixerSafe>("Safe Path Fixer");
    }

    private void OnGUI()
    {
        GUILayout.Label("AnimatorControllerの安全パス修正", EditorStyles.boldLabel);
        rootObject = (GameObject)EditorGUILayout.ObjectField("Root GameObject", rootObject, typeof(GameObject), true);
        animatorController = (AnimatorController)EditorGUILayout.ObjectField("Animator Controller", animatorController, typeof(AnimatorController), false);

        if (GUILayout.Button("全クリップ安全修正実行"))
        {
            FixAllClips();
        }

        if (remapLog.Count > 0)
        {
            GUILayout.Label("自動修正ログ:", EditorStyles.boldLabel);
            foreach (var log in remapLog)
                GUILayout.Label($"<color=red>{log.Key}</color> → <color=green>{log.Value}</color>", new GUIStyle(EditorStyles.label) { richText = true });
        }
        if (warnLog.Count > 0)
        {
            GUILayout.Label("修正できなかった/要注意ログ:", EditorStyles.boldLabel);
            foreach (var log in warnLog)
                GUILayout.Label($"<color=red>{log.Key}</color> => <color=orange>{log.Value}</color>", new GUIStyle(EditorStyles.label) { richText = true });
        }
    }

    void FixAllClips()
    {
        if (rootObject == null || animatorController == null)
        {
            EditorUtility.DisplayDialog("エラー", "Root GameObjectとAnimator Controllerを設定してください", "OK");
            return;
        }
        remapLog.Clear();
        warnLog.Clear();

        // コントローラー内の全アニメーションクリップを取得
        var clips = new HashSet<AnimationClip>();
        foreach (var layer in animatorController.layers)
        {
            foreach (var state in layer.stateMachine.states)
            {
                var motion = state.state.motion;
                if (motion is AnimationClip clip)
                    clips.Add(clip);
            }
        }

        int totalRemapped = 0;
        foreach (var clip in clips)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                var oldPath = binding.path;
                // もともとHierarchy上にいる場合は無視
                if (rootObject.transform.Find(oldPath) != null)
                    continue;

                // パス探索：完全一致、親部分一致優先、最後に末尾名一致
                var newPath = FindBestPath(rootObject.transform, oldPath, binding.propertyName);

                if (!string.IsNullOrEmpty(newPath) && newPath != oldPath)
                {
                    // 型一致判定：targetPathに対応Componentがあるか
                    var targetObj = rootObject.transform.Find(newPath)?.gameObject;
                    if (targetObj != null && IsPropertyMatch(binding.propertyName, targetObj))
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        var newBinding = binding;
                        newBinding.path = newPath;
                        AnimationUtility.SetEditorCurve(clip, binding, null);
                        AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                        remapLog[oldPath] = newPath;
                        totalRemapped++;
                    }
                    else
                    {
                        warnLog[oldPath] = $"型不一致 or 見つからず: {newPath}";
                    }
                }
                else
                {
                    warnLog[oldPath] = "該当パス見つからず";
                }
            }
        }

        EditorUtility.DisplayDialog("完了", $"修正完了（{totalRemapped}件リマップ, {warnLog.Count}件警告）", "OK");
    }

    // パス探索：親階層を部分一致するものを優先し、最悪は末尾名一致で代用
    string FindBestPath(Transform root, string lostPath, string propertyName)
    {
        var lostNames = lostPath.Split('/');
        var allCandidates = GetAllDescendants(root)
            .Select(t => new { t, path = GetRelativePath(root, t) })
            .ToList();

        // 完全一致
        var exact = allCandidates.FirstOrDefault(c => c.path == lostPath);
        if (exact != null) return exact.path;

        // 親階層部分一致
        foreach (var candidate in allCandidates)
        {
            var candNames = candidate.path.Split('/');
            if (candNames.Length >= lostNames.Length &&
                candNames.Skip(candNames.Length - lostNames.Length).SequenceEqual(lostNames))
                return candidate.path;
        }

        // 末尾名一致かつ型一致
        foreach (var candidate in allCandidates)
        {
            if (candidate.t.name == lostNames.Last() && IsPropertyMatch(propertyName, candidate.t.gameObject))
                return candidate.path;
        }

        // 末尾名一致（型チェックなし）
        foreach (var candidate in allCandidates)
            if (candidate.t.name == lostNames.Last())
                return candidate.path;

        return "";
    }

    // 型判定：プロパティ名とGameObjectのComponentでざっくり判断
    bool IsPropertyMatch(string propertyName, GameObject go)
    {
        // 代表的なプロパティ名で型推定
        if (propertyName.Contains("Particle"))
            return go.GetComponent<ParticleSystem>() != null;
        if (propertyName.Contains("Renderer") || propertyName.Contains("Mesh"))
            return go.GetComponent<Renderer>() != null;
        if (propertyName.Contains("Transform") || propertyName.Contains("Position") || propertyName.Contains("Rotation"))
            return true; // Transformは必ず存在
        // それ以外は、まあOKで返す（厳密化したい場合はここ拡張）
        return true;
    }

    // ツリー全Transform列挙
    List<Transform> GetAllDescendants(Transform root)
    {
        var list = new List<Transform>();
        foreach (Transform child in root)
        {
            list.Add(child);
            list.AddRange(GetAllDescendants(child));
        }
        return list;
    }

    string GetRelativePath(Transform root, Transform target)
    {
        var path = new List<string>();
        var t = target;
        while (t != root && t != null)
        {
            path.Insert(0, t.name);
            t = t.parent;
        }
        return string.Join("/", path);
    }
}
