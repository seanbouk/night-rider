// Draws a [LogRange] float as a log-spaced slider plus a value field showing the
// real number, so a 0.001..0.01 range feels even across the slider.

using UnityEditor;
using UnityEngine;
using NightRider.World;

[CustomPropertyDrawer(typeof(LogRangeAttribute))]
public class LogRangeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.Float)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        var attr = (LogRangeAttribute)attribute;
        float logMin = Mathf.Log10(attr.min);
        float logMax = Mathf.Log10(attr.max);

        EditorGUI.BeginProperty(position, label, property);
        Rect r = EditorGUI.PrefixLabel(position, label);

        const float fieldW = 64f, gap = 4f;
        var sliderRect = new Rect(r.x, r.y, r.width - fieldW - gap, r.height);
        var fieldRect  = new Rect(r.xMax - fieldW, r.y, fieldW, r.height);

        float logCur = Mathf.Log10(Mathf.Clamp(property.floatValue, attr.min, attr.max));

        EditorGUI.BeginChangeCheck();
        logCur = GUI.HorizontalSlider(sliderRect, logCur, logMin, logMax);
        float val = EditorGUI.FloatField(fieldRect, Mathf.Pow(10f, logCur));
        if (EditorGUI.EndChangeCheck())
            property.floatValue = Mathf.Clamp(val, attr.min, attr.max);

        EditorGUI.EndProperty();
    }
}
