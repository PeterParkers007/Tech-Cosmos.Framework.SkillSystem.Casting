using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TechCosmos.SkillSystem.Runtime;
using UnityEditor;
using UnityEngine;

namespace TechCosmos.SkillSystem.Casting.Editor
{
    /// <summary>
    /// 为本包 <see cref="SkillCastMiddleware{T}"/> 生成封闭到所选 IUnit 的中间件类。
    /// 不走技能框架 Generate All（那个目录会被核心生成器清空）。
    /// </summary>
    public sealed class GenerateCastMiddlewareWindow : EditorWindow
    {
        const string OutputFolder = "Assets/Generated/Casting";
        const string GeneratedNamespace = "TechCosmos.SkillSystem.Casting.Generated.Middlewares";

        Type _selectedUnitType;
        readonly List<Type> _unitTypes = new();
        Vector2 _scrollPos;
        string _searchFilter = "";
        bool _typesDirty = true;

        [MenuItem("Tech-Cosmos/SkillSystem Casting/Generate Cast Middleware", priority = 20)]
        public static void Open()
        {
            var window = GetWindow<GenerateCastMiddlewareWindow>("生成 Cast 中间件");
            window.minSize = new Vector2(420, 360);
            window.Show();
        }

        void OnEnable()
        {
            _typesDirty = true;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("生成 Cast 中间件", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "选择项目里的 IUnit 实现。生成 SkillCastMiddleware<该类型> 的封闭类，数值层会出现前摇 / 引导 / 可打断三个键。",
                MessageType.Info);
            EditorGUILayout.Space(4);

            DrawUnitTypePicker();

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(_selectedUnitType == null))
            {
                if (GUILayout.Button("生成", GUILayout.Height(28)))
                    GenerateSelected();
            }
        }

        void DrawUnitTypePicker()
        {
            RefreshUnitTypes();

            EditorGUILayout.LabelField("目标 IUnit 类型", EditorStyles.boldLabel);

            var searchStyle = new GUIStyle("SearchTextField");
            EditorGUILayout.BeginHorizontal();
            _searchFilter = EditorGUILayout.TextField(_searchFilter, searchStyle);
            if (GUILayout.Button("✕", GUILayout.Width(25)))
                _searchFilter = "";
            EditorGUILayout.EndHorizontal();

            var filtered = string.IsNullOrEmpty(_searchFilter)
                ? _unitTypes
                : _unitTypes.Where(t => t.Name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("没有找到实现 IUnit<> 的具体类。", MessageType.Warning);
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.MinHeight(180));
            foreach (var unitType in filtered)
            {
                bool selected = _selectedUnitType == unitType;
                if (selected)
                {
                    var rect = EditorGUILayout.BeginHorizontal();
                    EditorGUI.DrawRect(rect, new Color(0.3f, 0.5f, 0.8f, 0.3f));
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                }

                string displayName = unitType.Name;
                if (!string.IsNullOrEmpty(unitType.Namespace))
                    displayName += $"  ({unitType.Namespace})";

                if (GUILayout.Toggle(selected, displayName, EditorStyles.label, GUILayout.ExpandWidth(true)))
                    _selectedUnitType = unitType;

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (_selectedUnitType != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("将生成", $"{CleanName(_selectedUnitType)}SkillCastMiddleware");
                EditorGUILayout.LabelField("目录", OutputFolder);
            }
        }

        void RefreshUnitTypes()
        {
            if (!_typesDirty)
                return;

            _unitTypes.Clear();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;

                Type[] types;
                try { types = assembly.GetExportedTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
                        continue;
                    if (!type.GetInterfaces().Any(i =>
                            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUnit<>)))
                        continue;
                    _unitTypes.Add(type);
                }
            }

            _unitTypes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            _typesDirty = false;
        }

        void GenerateSelected()
        {
            if (_selectedUnitType == null)
            {
                EditorUtility.DisplayDialog("生成失败", "请选择一个 IUnit 类型。", "确定");
                return;
            }

            if (!Directory.Exists(OutputFolder))
                Directory.CreateDirectory(OutputFolder);

            string unitName = CleanName(_selectedUnitType);
            string className = unitName + "SkillCastMiddleware";
            string filePath = Path.Combine(OutputFolder, className + ".g.cs").Replace("\\", "/");
            File.WriteAllText(filePath, BuildCode(_selectedUnitType, unitName, className), Encoding.UTF8);
            AssetDatabase.Refresh();

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(filePath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }

            EditorUtility.DisplayDialog(
                "已生成",
                $"{className}\n\n把它加进 GlobalMiddlewareRegistry。核心 Generate All 不会动这个目录。",
                "确定");
        }

        static string BuildCode(Type unitType, string unitName, string className)
        {
            string unitNs = unitType.Namespace;
            string unitUsing = string.IsNullOrEmpty(unitNs) || unitNs == "TechCosmos.SkillSystem.Casting"
                ? string.Empty
                : $"\nusing {unitNs};";

            return $@"// <auto-generated/>
// 生成时间: {DateTime.Now:yyyy/MM/dd HH:mm:ss}
// 类型: SkillCastMiddleware<{unitName}>
// 请勿手动修改此文件

using System;
using TechCosmos.SkillSystem.Casting;{unitUsing}

namespace {GeneratedNamespace}
{{
    [Serializable]
    public class {className} : SkillCastMiddleware<{unitName}>
    {{
    }}
}}
";
        }

        static string CleanName(Type type)
        {
            string name = type.Name;
            int tick = name.IndexOf('`');
            return tick >= 0 ? name.Substring(0, tick) : name;
        }
    }
}
