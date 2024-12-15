using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimplyLocalize.Editor.Preparation;
using SimplyLocalize.Runtime.Data;
using SimplyLocalize.Runtime.Data.Extensions;
using SimplyLocalize.Runtime.Data.Keys;
using SimplyLocalize.Runtime.Data.Serializable;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Keys
{
    public class LocalizationKeysDataEditorWindow : EditorWindow
    {
        private const int _LINE_HEIGHT = 30;
        private const int _MAX_FIELD_HEIGHT = 400; 
        
        private int _selectedMainTab;
        private readonly string[] _mainTabs = { "Language", "Keys", "Text Translating"};//, "Sprites", "Audio" };
        
        private string _newKeys = "";
        
        private Vector2 _windowScrollPosition;
        private Vector2 _keysScrollPosition;
        private Vector2 _multipleKeysScrollPosition;

        private GUIStyle _labelStyle;
        private GUIStyle _keyStyle;
        private GUIStyle _textAreaStyle;
        private GUIStyle _buttonStyle;
        
        private LocalizationKeysData _localizationKeysData;
        private List<LocalizationData> _languages;
        private List<FontHolder> _fontHolders;
        
        private string _newLanguage;
        private FontHolder _newLanguageFontHolder;
        
        private string _newFontHolderName;
        private Font _newFont;
        private TMP_FontAsset _newTMPFont;

        private string _newKey;
        
        private int _selectedLanguageTab;
        
        private Object _newObjectKey;

        [MenuItem("Window/SimplyLocalize/Localization Settings", priority = 300, secondaryPriority = 2)]
        private static void ShowWindow()
        {
            var window = GetWindow<LocalizationKeysDataEditorWindow>();
            window.titleContent = new GUIContent("Localization Settings");
            window.Show();
        }

        private void OnGUI()
        {
            if (TryInitializeKeysData() == false) return;
            
            SetupStyles();
            
            _windowScrollPosition = GUILayout.BeginScrollView(_windowScrollPosition); 
            
            DrawSelectionTabs();
            
            GUILayout.EndScrollView();

            if (GUI.changed)
            {
                EditorUtility.SetDirty(_localizationKeysData);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private bool TryInitializeKeysData()
        {
            if (_localizationKeysData != null) 
                return true;
            
            _localizationKeysData = LocalizeEditor.GetLocalizationKeysData();

            if (_localizationKeysData != null) 
                return true;
            
            _localizationKeysData = (LocalizationKeysData)EditorGUILayout.ObjectField(
                "Localization Keys Data",
                _localizationKeysData,
                typeof(LocalizationKeysData),
                false
            );

            EditorGUILayout.HelpBox("Please assign a LocalizationKeysData asset to edit.", MessageType.Error);

            return false;

        }

        private void SetupStyles()
        {
            _labelStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                fixedHeight = _LINE_HEIGHT,
                wordWrap = true,
                normal = new GUIStyleState
                {
                    textColor = Color.white
                }
            };

            _keyStyle = new GUIStyle(GUI.skin.textField)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 15,
                fixedHeight = _LINE_HEIGHT,
            };

            _textAreaStyle = new GUIStyle(GUI.skin.textArea)
            {
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 15,
            };
            
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                fixedHeight = _LINE_HEIGHT,
            };
        }

        private void DrawSelectionTabs()
        {
            _selectedMainTab = GUILayout.Toolbar(_selectedMainTab, _mainTabs, _buttonStyle);
            
            switch (_selectedMainTab)
            {
                case 0: 
                    DrawLanguageTab();
                    DrawGenerateButton();
                    break;
                
                case 1: 
                    DrawKeys();
                    DrawAddMultipleKeys();
                    DrawGenerateButton();
                    
                    break;
                case 2:
                    DrawTranslating();
                    break;
                
                case 3:
                    // DrawSprites();
                    break;
                
                case 4:
                    // DrawAudio();
                    break;
            }
        }

        private void DrawLanguageTab()
        {
            DrawDefaultLanguageData();
            DrawLanguageSetting();
            DrawFontSetting();
        }

        private void DrawDefaultLanguageData()
        {
            EditorGUILayout.Space(_LINE_HEIGHT);
            EditorGUILayout.LabelField("Default Language", _labelStyle);
            
            _localizationKeysData.DefaultLocalizationData = (LocalizationData)EditorGUILayout.ObjectField(
                _localizationKeysData.DefaultLocalizationData, 
                typeof(LocalizationData),
                false,
                GUILayout.Height(_LINE_HEIGHT)
            );
            
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }

        private void DrawLanguageSetting()
        {
            EditorGUILayout.LabelField("Language Setting", _labelStyle);
            
            _languages ??= Resources.LoadAll<LocalizationData>("").ToList();
            _languages = _languages.Where(x => x != null).ToList();
            
            for (var i = 0; i < _languages.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                var language = _languages[i];

                EditorGUI.BeginDisabledGroup(true);
                language.i18nLang = EditorGUILayout.TextField(language.i18nLang, _keyStyle, GUILayout.Height(_LINE_HEIGHT));
                EditorGUI.EndDisabledGroup();
                
                language.OverrideFontAsset = (FontHolder)EditorGUILayout.ObjectField(language.OverrideFontAsset, typeof(FontHolder), false, GUILayout.Height(_LINE_HEIGHT));
                
                if (GUILayout.Button("Remove Language", _buttonStyle, GUILayout.Height(_LINE_HEIGHT)))
                {
                    _languages.Remove(language);
                    i--;
                    
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(language));
                }
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.BeginHorizontal();

            var hasDoubles = _languages.Count(l => l.i18nLang == _newLanguage) > 0;

            if (hasDoubles)
            {
                GUI.color = Color.red;
            }
            
            _newLanguage = EditorGUILayout.TextField(_newLanguage, _keyStyle, GUILayout.Height(_LINE_HEIGHT));
            _newLanguageFontHolder = (FontHolder)EditorGUILayout.ObjectField(_newLanguageFontHolder, typeof(FontHolder), false, GUILayout.Height(_LINE_HEIGHT));

            var newLanguageIsEmpty = _newLanguage == "";
            if (newLanguageIsEmpty)
            {
                EditorGUI.BeginDisabledGroup(true);
            }
            
            if (GUILayout.Button("Add New Language", _buttonStyle, GUILayout.Height(_LINE_HEIGHT)))
            {
                var newLanguage = CreateInstance<LocalizationData>();
                newLanguage.name = $"LocalizationData_{_newLanguage}";
                newLanguage.i18nLang = _newLanguage;
                newLanguage.OverrideFontAsset = _newLanguageFontHolder;
            
                var newLanguageAssetPath = Path.Combine(LocalizationPreparation.LocalizationResourcesPath, newLanguage.name);
                var dataPath = Path.ChangeExtension(newLanguageAssetPath, LocalizationPreparation.FileExtensionAsset);

                AssetDatabase.CreateAsset(newLanguage, dataPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
        
                _languages.Add(newLanguage);
                _newLanguage = "";
                _newLanguageFontHolder = null;
            }

            if (newLanguageIsEmpty)
            {
                EditorGUI.EndDisabledGroup();
            }
            
            if (hasDoubles)
            {
                GUI.color = Color.white;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFontSetting()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            EditorGUILayout.LabelField("Font Setting", _labelStyle);
            
            _fontHolders ??= Resources.LoadAll<FontHolder>("").ToList();
            _fontHolders = _fontHolders.Where(x => x != null).ToList();
            
            for (var i = 0; i < _fontHolders.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                var fontHolder = _fontHolders[i];

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(fontHolder, typeof(FontHolder), false, GUILayout.Height(_LINE_HEIGHT));
                EditorGUI.EndDisabledGroup();
                
                fontHolder.TMPFont = (TMP_FontAsset)EditorGUILayout.ObjectField(fontHolder.TMPFont, typeof(TMP_FontAsset), false, GUILayout.Height(_LINE_HEIGHT));
                fontHolder.LegacyFont = (Font)EditorGUILayout.ObjectField(fontHolder.LegacyFont, typeof(Font), false, GUILayout.Height(_LINE_HEIGHT));
                
                if (GUILayout.Button("Remove Font Holder", _buttonStyle, GUILayout.Height(_LINE_HEIGHT)))
                {
                    _fontHolders.Remove(fontHolder);
                    i--;
                    
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(fontHolder));
                }
                
                EditorGUILayout.EndHorizontal();
            }

                
            EditorGUILayout.BeginHorizontal();

            var hasDoubles = _fontHolders.Count(l => l.name == _newFontHolderName) > 0;

            if (hasDoubles)
            {
                GUI.color = Color.red;
            }
        
            _newFontHolderName = EditorGUILayout.TextField(_newFontHolderName, _keyStyle, GUILayout.Height(_LINE_HEIGHT));
            _newFont = (Font)EditorGUILayout.ObjectField(_newFont, typeof(Font), false, GUILayout.Height(_LINE_HEIGHT));
            _newTMPFont = (TMP_FontAsset)EditorGUILayout.ObjectField(_newTMPFont, typeof(TMP_FontAsset), false, GUILayout.Height(_LINE_HEIGHT));

            var newFontHolderNameIsEmpty = _newFontHolderName == "";
            if (newFontHolderNameIsEmpty)
            {
                EditorGUI.BeginDisabledGroup(true);
            }
        
            if (GUILayout.Button("Add New Font Holder", _buttonStyle, GUILayout.Height(_LINE_HEIGHT)))
            {
                var newFontHolder = CreateInstance<FontHolder>();
                newFontHolder.name = $"LocalizationFontHolder_{_newFontHolderName}";
                newFontHolder.LegacyFont = _newFont;
                newFontHolder.TMPFont = _newTMPFont;
        
                var newFontHolderAssetPath = Path.Combine(LocalizationPreparation.LocalizationResourcesPath, newFontHolder.name);
                var dataPath = Path.ChangeExtension(newFontHolderAssetPath, LocalizationPreparation.FileExtensionAsset);

                AssetDatabase.CreateAsset(newFontHolder, dataPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
    
                _fontHolders.Add(newFontHolder);
                _newFontHolderName = "";
                _newFont = null;
                _newTMPFont = null;
            }

            if (newFontHolderNameIsEmpty)
            {
                EditorGUI.EndDisabledGroup();
            }
        
            if (hasDoubles)
            {
                GUI.color = Color.white;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawKeys()
        {
            var scrollHeight = (_localizationKeysData.Keys.Count + 1) * _LINE_HEIGHT;
            if (scrollHeight > _MAX_FIELD_HEIGHT)
            {
                scrollHeight = _MAX_FIELD_HEIGHT;
            }
            
            EditorGUILayout.Space(_LINE_HEIGHT);
            _keysScrollPosition = GUILayout.BeginScrollView(_keysScrollPosition, GUILayout.Height(scrollHeight));

            for (var i = 0; i < _localizationKeysData.Keys.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                var keyText = _localizationKeysData.Keys[i];
                
                var hasDoubles = HasDoubles(keyText);
                if (hasDoubles)
                {
                    GUI.color = Color.red;
                }
                
                _localizationKeysData.Keys[i] = EditorGUILayout.TextField(keyText, _keyStyle, GUILayout.Height(_LINE_HEIGHT));

                if (hasDoubles)
                {
                    GUI.color = Color.white;
                }
                
                _localizationKeysData.Keys[i] = _localizationKeysData.Keys[i].ToEnumName();

                if (GUILayout.Button("Remove", _buttonStyle, GUILayout.Width(70), GUILayout.Height(_LINE_HEIGHT)))
                {
                    _localizationKeysData.Keys.RemoveAt(i);
                    i--; 
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();
            }

            GUILayout.EndScrollView();
            
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            
            _newKey = EditorGUILayout.TextField(_newKey, _keyStyle, GUILayout.Height(_LINE_HEIGHT));
            
            if (_newKey == "")
            {
                EditorGUI.BeginDisabledGroup(true);
            }
            
            if (GUILayout.Button("Add New Key", _buttonStyle,GUILayout.Height(_LINE_HEIGHT)))
            {
                _localizationKeysData.Keys.Add(_newKey);
                _keysScrollPosition = new Vector2(0, _MAX_FIELD_HEIGHT);
                
                _newKey = "";
            }
            
            if (_newKey == "")
            {
                EditorGUI.EndDisabledGroup();
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private bool HasDoubles(string keyText)
        {
            return _localizationKeysData.Keys.Count(l => l == keyText) > 1;
        }

        private bool HasDoubles()
        {
            return _localizationKeysData.Keys.GroupBy(n => n).Any(g => g.Count() > 1);
        }

        private void DrawAddMultipleKeys()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space();
            
            GUILayout.Label("Add multiple keys (separated by commas or line breaks):", _labelStyle);
            
            var keysArray = _newKeys.Split(new[] { ',', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            
            var scrollHeight = (keysArray.Length) * _LINE_HEIGHT;
            
            if (scrollHeight > _MAX_FIELD_HEIGHT)
            {
                scrollHeight = _MAX_FIELD_HEIGHT;
            }
            
            _multipleKeysScrollPosition = GUILayout.BeginScrollView(_multipleKeysScrollPosition, GUILayout.Height(scrollHeight));
            
            _newKeys = EditorGUILayout.TextArea(_newKeys, _textAreaStyle);
            
            GUILayout.EndScrollView();

            if (keysArray.Length == 0)
            {
                EditorGUI.BeginDisabledGroup(true);
            }
                
            if (GUILayout.Button("Add Range", _buttonStyle,GUILayout.Height(_LINE_HEIGHT)))
            {
                AddKeys(keysArray);
            }
            
            if (keysArray.Length == 0)
            {
                EditorGUI.EndDisabledGroup();
            }
        }

        private void AddKeys(string[] keysArray)
        {
            for (var i = 0; i < keysArray.Length; i++)
            {
                keysArray[i] = keysArray[i].Trim();
            }

            foreach (var key in keysArray)
            {
                var keyName = key.ToEnumName();
                if (keyName == "")
                {
                    continue;
                }
                
                _localizationKeysData.Keys.Add(keyName);
            }

            _newKeys = "";

            EditorUtility.SetDirty(_localizationKeysData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void DrawGenerateButton()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            
            var canGenerate = HasDoubles() == false;
            if (canGenerate)
            {
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.red;
                EditorGUI.BeginDisabledGroup(true);
            }

            if (GUILayout.Button("Generate Keys", _buttonStyle,GUILayout.Height(_LINE_HEIGHT)))
            {
                if (canGenerate)
                {
                    LocalizeEditor.GenerateLocalizationKeys();
                }
                else
                {
                    GUI.color = Color.white;
                }
            }

            if (!canGenerate)
            {
                EditorGUI.BeginDisabledGroup(true);
            }
            
            GUI.color = Color.white;
        }

        private void DrawTranslating()
        {
            if (DrawTranslatingLanguageTabs() == false) return;

            var language = _languages[_selectedLanguageTab].i18nLang;

            if (_localizationKeysData.Translations.TryGetValue(language, out var translating))
            {
                for (var i = 0; i < translating.Count; i++)
                {
                    var key = translating.Keys.ElementAt(i);

                    EditorGUILayout.BeginHorizontal();
                    
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.TextField(key, _keyStyle, GUILayout.Height(_LINE_HEIGHT));
                    EditorGUI.EndDisabledGroup();

                    translating[key] = EditorGUILayout.TextField(translating[key], _keyStyle, GUILayout.Height(_LINE_HEIGHT));
                    
                    EditorGUILayout.EndHorizontal();
                }
                
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                if (GUILayout.Button("Save to JSON", _buttonStyle,GUILayout.Height(_LINE_HEIGHT)))
                {
                    LocalizeEditor.GenerateLocalizationJson(_localizationKeysData.Translations);
                }
            }
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("No translating found", MessageType.Info);
            }
            
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Load from JSON", _buttonStyle,GUILayout.Height(_LINE_HEIGHT)))
            {
                _localizationKeysData.Translations = LocalizeEditor.GetAllLocalizationsDictionary();
            }
        }

        private bool DrawTranslatingLanguageTabs()
        {
            if (_languages == null || _languages.Count == 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("No languages found", MessageType.Info );
                return false;
            }
            
            EditorGUILayout.Space();
            
            _selectedLanguageTab = GUILayout.Toolbar(_selectedLanguageTab, _languages.Select(l => l.i18nLang).ToArray(), _buttonStyle);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space();
            
            return true;
        }

        private void DrawSprites()
        {
            if (DrawTranslatingLanguageTabs() == false) return;

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            _newObjectKey = EditorGUILayout.ObjectField(_newObjectKey, typeof(Object), false, GUILayout.Height(_LINE_HEIGHT));
            if (GUILayout.Button("Add", _buttonStyle,GUILayout.Height(_LINE_HEIGHT)))
            {
                foreach (var lang in _languages)
                {
                    if (_localizationKeysData.ObjectsTranslations.TryGetValue(lang.i18nLang, out _) == false)
                    {
                        _localizationKeysData.ObjectsTranslations.Add(lang.i18nLang, new SerializableSerializableDictionary<Object, Object>());
                    }
                    
                    _localizationKeysData.ObjectsTranslations[lang.i18nLang].Add(_newObjectKey, null);
                }
                
                _newObjectKey = null;
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            
            var language = _languages[_selectedLanguageTab].i18nLang;
            
            if (_localizationKeysData.ObjectsTranslations.TryGetValue(language, out var translating))
            {
                for (var i = 0; i < translating.Count; i++)
                {
                    var key = translating.ElementAt(i).Key;
                    var height = _LINE_HEIGHT * 4;
                    var width = _LINE_HEIGHT * 4;

                    EditorGUILayout.BeginHorizontal();
                    
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ObjectField(key, key.GetType(), false, GUILayout.Height(height), GUILayout.Width(width));
                    EditorGUI.EndDisabledGroup();

                    translating[key] = EditorGUILayout.ObjectField(translating[key], key.GetType(), false, GUILayout.Height(height), GUILayout.Width(width));
                    
                    if (GUILayout.Button("Remove", _buttonStyle,GUILayout.Height(_LINE_HEIGHT)))
                    {
                        foreach (var lang in _languages)
                        {
                            _localizationKeysData.ObjectsTranslations[lang.i18nLang].Remove(key);
                        }

                        i--;
                    }
                    
                    EditorGUILayout.EndHorizontal();   
                }
                
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            }
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("No translating objects found", MessageType.Info);
            }
            
            EditorGUILayout.Space();
        }
    }
}
