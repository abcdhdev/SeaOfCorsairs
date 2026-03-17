using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerDirectionSpriteController))]
public class PlayerDirectionSpriteControllerEditor : Editor
{
    private const int FourWayDiagonalMode = 0;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty directionMode = serializedObject.FindProperty("directionMode");
        EditorGUILayout.PropertyField(directionMode);

        bool isEightWay = directionMode.enumValueIndex != FourWayDiagonalMode;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Normal Sprites", EditorStyles.boldLabel);
        DrawDirectionalSpriteFields(isEightWay, false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Burning Sprites", EditorStyles.boldLabel);
        DrawDirectionalSpriteFields(isEightWay, true);

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useBurningSprites"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useXZPlane"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lockWorldRotation"));

        SerializedProperty worldEulerRotation = serializedObject.FindProperty("worldEulerRotation");
        if (serializedObject.FindProperty("lockWorldRotation").boolValue)
        {
            EditorGUILayout.PropertyField(worldEulerRotation);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDirectionalSpriteFields(bool isEightWay, bool burning)
    {
        if (isEightWay)
        {
            DrawSpriteField("upSprite", "Up", burning);
            DrawSpriteField("upRightSprite", "Up Right", burning);
            DrawSpriteField("rightSprite", "Right", burning);
            DrawSpriteField("upLeftSprite", "Up Left", burning);
            DrawSpriteField("downSprite", "Bottom", burning);
            DrawSpriteField("downRightSprite", "Bottom Right", burning);
            DrawSpriteField("leftSprite", "Left", burning);
            DrawSpriteField("downLeftSprite", "Bottom Left", burning);
            return;
        }

        DrawSpriteField("upLeftSprite", "Up Left", burning);
        DrawSpriteField("upRightSprite", "Up Right", burning);
        DrawSpriteField("downLeftSprite", "Bottom Left", burning);
        DrawSpriteField("downRightSprite", "Bottom Right", burning);
    }

    private void DrawSpriteField(string basePropertyName, string label, bool burning)
    {
        string propertyName = burning ? BuildBurningPropertyName(basePropertyName) : basePropertyName;
        string displayLabel = burning ? $"Burning {label}" : label;
        EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName), new GUIContent(displayLabel));
    }

    private static string BuildBurningPropertyName(string basePropertyName)
    {
        string trimmedName = basePropertyName.EndsWith("Sprite")
            ? basePropertyName.Substring(0, basePropertyName.Length - "Sprite".Length)
            : basePropertyName;

        return $"burning{char.ToUpperInvariant(trimmedName[0])}{trimmedName.Substring(1)}Sprite";
    }
}
