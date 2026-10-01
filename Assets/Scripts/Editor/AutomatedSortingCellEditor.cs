using UnityEditor;
using UnityEngine;

public static class AutomatedSortingCellEditor
{
    [MenuItem("Digital Twin/Setup Automated Sorting Cell", false, 10)]
    public static void SetupSortingCell()
    {
        AutomatedSortingCellTwin existing = Object.FindFirstObjectByType<AutomatedSortingCellTwin>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            EditorUtility.DisplayDialog("Sorting Cell Digital Twin", "Automated Sorting Cell Twin is already present in this scene!", "OK");
            return;
        }

        GameObject cellObj = new GameObject("AutomatedSortingCellTwin");
        cellObj.AddComponent<AutomatedSortingCellTwin>();
        Selection.activeGameObject = cellObj;
        Undo.RegisterCreatedObjectUndo(cellObj, "Create Automated Sorting Cell Twin");

        EditorUtility.DisplayDialog(
            "Sorting Cell Digital Twin",
            "Automated Sorting Cell Twin created successfully!\n\nPress PLAY to begin the live digital twin simulation with kinematic conveyor transport, machine vision inspection, pneumatic sorting, and live SCADA HUD telemetry.",
            "Awesome!");
    }
}
