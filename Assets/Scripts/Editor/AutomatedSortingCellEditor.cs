using UnityEditor;
using UnityEngine;

public static class AutomatedSortingCellEditor
{
    [MenuItem("realvirtual/Setup Automated Sorting Cell Digital Twin", false, 10)]
    public static void SetupSortingCell()
    {
        AutomatedSortingCellTwin existing = Object.FindFirstObjectByType<AutomatedSortingCellTwin>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            EditorUtility.DisplayDialog("realvirtual Digital Twin", "Automated Sorting Cell Twin is already present in this scene!", "OK");
            return;
        }

        GameObject cellObj = new GameObject("AutomatedSortingCellTwin");
        cellObj.AddComponent<AutomatedSortingCellTwin>();
        Selection.activeGameObject = cellObj;
        Undo.RegisterCreatedObjectUndo(cellObj, "Create Automated Sorting Cell Twin");

        EditorUtility.DisplayDialog(
            "realvirtual Digital Twin",
            "Automated Sorting Cell Twin created successfully!\n\nPress PLAY to begin the live digital twin simulation with real-time pneumatic pusher, machine vision inspection, and realvirtual MCP AI integration.",
            "Awesome!");
    }
}
