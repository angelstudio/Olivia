using System;
using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension { 
    internal static class GUIUtilities {
        public class GUIEnabledBlock : IDisposable {
            private bool enabled;

            public GUIEnabledBlock(bool enabled) {
                if(enabled) return;
                this.enabled = enabled;

                GUI.enabled = false;
            }

            public void Dispose() {
                if(enabled) return;

                GUI.enabled = true;
            }
        }

        public class Horizontal : IDisposable {
            public Horizontal() {
                EditorGUILayout.BeginHorizontal();
            }

            public Horizontal(params GUILayoutOption[] options) {
                EditorGUILayout.BeginHorizontal(options);
            }

            public Horizontal(GUIStyle style) {
                EditorGUILayout.BeginHorizontal(style);
            }

            public virtual void Dispose(bool disposing) {
                Dispose();
            }

            public void Dispose() {
                EditorGUILayout.EndHorizontal();
            }
        }

        public class Vertical : IDisposable {
            public Vertical() {
                EditorGUILayout.BeginVertical();
            }

            public Vertical(params GUILayoutOption[] options) {
                EditorGUILayout.BeginVertical(options);
            }

            public Vertical(GUIStyle style) {
                EditorGUILayout.BeginVertical(style);
            }

            public virtual void Dispose(bool disposing) {
                Dispose();
            }

            public void Dispose() {
                EditorGUILayout.EndHorizontal();
            }
        }
        
        public static void DrawFillAndRightControl(Action<Rect> fillControl, Action<Rect> rightControl = null, GUIContent labelContent = null, int rightControlWidth = 0) {
            Rect baseRect = EditorGUILayout.GetControlRect();

            if(labelContent != null) {
                GUI.Label(baseRect, labelContent);
            }

            Rect fillRect = new Rect(baseRect);
            if(labelContent != null) {
                fillRect.xMin += EditorGUIUtility.labelWidth;
            }
            fillRect.xMax -= rightControlWidth;

            if(fillControl == null) {
                Debug.LogError("A \"Fill Control\" wasn't passed");
                return;
            }
            fillControl(fillRect);

            if(rightControl != null) {
                Rect rightControlRect = new Rect(baseRect);
                rightControlRect.xMin = fillRect.xMax + 4f;
                rightControl(rightControlRect);
            }
        }

        public static void DrawToggleAndFill(Action<Rect> toggleControl, Action<Rect> fillControl, bool enableFillControl, bool enableToggle) {
            Rect controlRect = EditorGUILayout.GetControlRect();
            
            Rect toggleRect = new Rect(controlRect);
            toggleRect.xMax = EditorGUIUtility.labelWidth;
            toggleRect.yMin -= 1f;
            using(new GUIEnabledBlock(enableToggle)) {
                toggleControl(toggleRect);
            }
                   
            if(enableFillControl == false) {
                GUI.enabled = false;
            }
            Rect fillRect = new Rect(controlRect);
            fillRect.xMin = EditorGUIUtility.labelWidth + 14f;
            fillControl(fillRect);

            if(enableFillControl == false) {
                GUI.enabled = true;
            }
        }

        public static void DrawToggleAndMinMax(Action<Rect> toggleControl, Action<Rect> minMaxSliderControl, Action<Rect> minFloatControl, Action<Rect> maxFloatControl, bool enableFillControl, bool enableToggle) {
            Rect controlRect = EditorGUILayout.GetControlRect();

            Rect toggleRect = new Rect(controlRect);
            toggleRect.xMax = EditorGUIUtility.labelWidth;
            toggleRect.yMin -= 1f;
            using(new GUIEnabledBlock(enableToggle)) {
                toggleControl(toggleRect);
            }

            Rect labelRect = new Rect(controlRect);
            labelRect.xMin += 14f;
            labelRect.xMax = EditorGUIUtility.labelWidth;
            
            if(enableFillControl == false) {
                GUI.enabled = false;
            }
            Rect fillRect = new Rect(controlRect);
            fillRect.xMin = EditorGUIUtility.labelWidth + 14;
            Rect leftRect = new Rect(fillRect);
            leftRect.xMax = leftRect.xMin + 50f;
            Rect middleRect = new Rect(fillRect);
            middleRect.xMin = leftRect.xMin + 55;
            middleRect.xMax = fillRect.xMax - 55f;
            Rect rightRect = new Rect(fillRect);
            rightRect.xMin = rightRect.xMax - 50;

            minFloatControl(leftRect);
            minMaxSliderControl(middleRect);
            maxFloatControl(rightRect);
            
            if(enableFillControl == false) {
                GUI.enabled = true;
            }
        }
    }
}