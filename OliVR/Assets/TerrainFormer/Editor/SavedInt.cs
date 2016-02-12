using System;
using UnityEditor;

namespace JesseStiller.TerrainFormerExtension { 
    internal class SavedInt {
        internal Action ValueChanged;

        private readonly string prefsKey;
        internal readonly int defaultValue;

        private int value;
        internal int Value {
            get {
                return value;
            }
            set {
                if(this.value == value) return;
                this.value = value;
                EditorPrefs.SetInt(prefsKey, value);

                if(ValueChanged != null) ValueChanged();
            }
        }

        public SavedInt(string prefsKey, int defaultValue) {
            this.prefsKey = prefsKey;
            this.defaultValue = defaultValue;
            value = EditorPrefs.GetInt(prefsKey, defaultValue);
        }

        public static implicit operator int(SavedInt s) {
            return s.Value;
        }
    }
}