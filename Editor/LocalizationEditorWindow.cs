using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor
{
    internal class LocalizationEditorWindow : EditorWindow
    {
        private const int _LINE_HEIGHT = LocalizationEditorStyles.LINE_HEIGHT;
        private const int _MAX_FIELD_HEIGHT = LocalizationEditorStyles.MAX_FIELD_HEIGHT; 
        
        private int _selectedMainTab;
        private readonly string[] _mainTabs = { "Language", "Keys", "Text Translating", "Sprites"};//, "Objects", "AudioClip" };
        
        private string _newKeys = "";

        private LocalizationConfig.SpaceUsage _spaceIsGroupSeparator;
        
        private Vector2 _windowScrollPosition;
        private Vector2 _keysScrollPosition;
        private Vector2 _multipleKeysScrollPosition;

        private static GUIStyle LabelStyle => LocalizationEditorStyles.LabelStyle;
        private static GUIStyle LabelStylePrefix => LocalizationEditorStyles.LabelStylePrefix;
        private static GUIStyle KeyStyle => LocalizationEditorStyles.KeyStyle;
        private static GUIStyle KeyStyleMultiline => LocalizationEditorStyles.KeyStyleMultiline;
        private static GUIStyle ToggleStyle => LocalizationEditorStyles.ToggleStyle;
        private static GUIStyle TextAreaStyle => LocalizationEditorStyles.TextAreaStyle;
        private static GUIStyle ButtonStyle => LocalizationEditorStyles.ButtonStyle;
        private static GUIStyle HorizontalStyle => LocalizationEditorStyles.HorizontalStyle;
        
        private LocalizationKeysData _localizationKeysData;
        private LocalizationConfig _localizationConfig;
        
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

        [MenuItem("Window/Simply Localize/Localization Settings", priority = 300, secondaryPriority = 2)]
        private static void ShowWindow()
        {
            var window = GetWindow<LocalizationEditorWindow>();
            window.titleContent = new GUIContent("Localization Settings");
            window.Show();
        }

        private void OnGUI()
        {
            if (TryInitializeKeysData() == false) return;
            if (TryInitializeLocalizationConfig() == false) return;
            
            _windowScrollPosition = GUILayout.BeginScrollView(_windowScrollPosition); 
            
            DrawSelectionTabs();
            
            GUILayout.EndScrollView();

            if (GUI.changed)
            {
                EditorUtility.SetDirty(_localizationKeysData);
                EditorUtility.SetDirty(_localizationConfig);
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

        private bool TryInitializeLocalizationConfig()
        {
            if (_localizationConfig != null) 
                return true;
            
            _localizationConfig = LocalizeEditor.GetLocalizationConfig();

            if (_localizationConfig != null) 
                return true;
            
            _localizationConfig = (LocalizationConfig)EditorGUILayout.ObjectField(
                "Localization Config",
                _localizationConfig,
                typeof(LocalizationConfig),
                false
            );

            EditorGUILayout.HelpBox("Please assign a LocalizationConfig asset to edit.", MessageType.Error);

            return false;
        }

        private void DrawSelectionTabs()
        {
            _selectedMainTab = GUILayout.Toolbar(_selectedMainTab, _mainTabs, ButtonStyle);
            
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
                    DrawObject(typeof(Sprite));
                    break;
                
                case 4:
                    DrawObject(typeof(AudioClip));
                    break;
            }
        }

        private void DrawLanguageTab()
        {
            DrawDefaultLanguageData();
            DrawLanguageSetting();
            DrawFontSetting();
            DrawSettings();
        }

        private void DrawDefaultLanguageData()
        {
            EditorGUILayout.Space(_LINE_HEIGHT);
            EditorGUILayout.LabelField("Default Language", LabelStyle);
            
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
            EditorGUILayout.LabelField("Language Setting", LabelStyle);

            _languages = LocalizeEditor.GetLanguages(true);
            
            for (var i = 0; i < _languages.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                var language = _languages[i];

                EditorGUI.BeginDisabledGroup(true);
                language.i18nLang = EditorGUILayout.TextField(language.i18nLang, KeyStyle, GUILayout.Height(_LINE_HEIGHT), GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.3f));
                EditorGUI.EndDisabledGroup();
                
                language.OverrideFontAsset = (FontHolder)EditorGUILayout.ObjectField(language.OverrideFontAsset, typeof(FontHolder), false, GUILayout.Height(_LINE_HEIGHT));

                if (GUI.changed)
                {
                    EditorUtility.SetDirty(language);
                }
                
                if (GUILayout.Button("Remove Language", ButtonStyle, GUILayout.Height(_LINE_HEIGHT), GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.3f)))
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
            
            _newLanguage = EditorGUILayout.TextField(_newLanguage, KeyStyle, GUILayout.Height(_LINE_HEIGHT), GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.3f));
            _newLanguageFontHolder = (FontHolder)EditorGUILayout.ObjectField(_newLanguageFontHolder, typeof(FontHolder), false, GUILayout.Height(_LINE_HEIGHT));

            var newLanguageIsEmpty = _newLanguage == "";
            if (newLanguageIsEmpty)
            {
                EditorGUI.BeginDisabledGroup(true);
            }
            
            if (GUILayout.Button("Add New Language", ButtonStyle, GUILayout.Height(_LINE_HEIGHT), GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.3f)))
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

            EditorGUILayout.LabelField("Font Setting", LabelStyle);
            
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
                
                if (GUILayout.Button("Remove Font Holder", ButtonStyle, GUILayout.Height(_LINE_HEIGHT)))
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
        
            _newFontHolderName = EditorGUILayout.TextField(_newFontHolderName, KeyStyle, GUILayout.Height(_LINE_HEIGHT));
            _newFont = (Font)EditorGUILayout.ObjectField(_newFont, typeof(Font), false, GUILayout.Height(_LINE_HEIGHT));
            _newTMPFont = (TMP_FontAsset)EditorGUILayout.ObjectField(_newTMPFont, typeof(TMP_FontAsset), false, GUILayout.Height(_LINE_HEIGHT));

            var newFontHolderNameIsEmpty = _newFontHolderName == "";
            if (newFontHolderNameIsEmpty)
            {
                EditorGUI.BeginDisabledGroup(true);
            }
        
            if (GUILayout.Button("Add New Font Holder", ButtonStyle, GUILayout.Height(_LINE_HEIGHT), GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.3f)))
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

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.LabelField("Settings", LabelStyle);
            

            // Space
            EditorGUILayout.BeginHorizontal(HorizontalStyle);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Space is:", LabelStylePrefix, GUILayout.Height(_LINE_HEIGHT));
            var options = Enum.GetValues(typeof(LocalizationConfig.SpaceUsage)).Cast<LocalizationConfig.SpaceUsage>().Select(x => x.ToString()).ToArray();
            
            _spaceIsGroupSeparator = _localizationConfig.SpaceIsGroupSeparator;
            _spaceIsGroupSeparator = (LocalizationConfig.SpaceUsage)EditorGUILayout.Popup("", (int)_spaceIsGroupSeparator, options, LocalizationEditorStyles.PopupStyle);
            _localizationConfig.SpaceIsGroupSeparator = _spaceIsGroupSeparator;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            // Logging
            EditorGUILayout.BeginHorizontal(HorizontalStyle);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Enable Logging", LabelStylePrefix, GUILayout.Height(_LINE_HEIGHT));
            _localizationConfig.EnableLogging = EditorGUILayout.Toggle(
                _localizationConfig.EnableLogging, 
                ToggleStyle,
                GUILayout.Height(_LINE_HEIGHT)
            );
            EditorGUILayout.EndHorizontal();
            
            // Enable Logging In Build
            EditorGUILayout.BeginHorizontal(HorizontalStyle);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Enable Logging In Build", LabelStylePrefix, GUILayout.Height(_LINE_HEIGHT));
            _localizationConfig.EnableLoggingInBuild = EditorGUILayout.Toggle(
                _localizationConfig.EnableLoggingInBuild, 
                ToggleStyle,
                GUILayout.Height(_LINE_HEIGHT)
            );
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            
            // Show Editor Language Changing Popup
            EditorGUILayout.BeginHorizontal(HorizontalStyle);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Show Editor Language Changing Popup", LabelStylePrefix, GUILayout.Height(_LINE_HEIGHT));
            _localizationConfig.ShowLanguagePopup = EditorGUILayout.Toggle(
                _localizationConfig.ShowLanguagePopup, 
                ToggleStyle,
                GUILayout.Height(_LINE_HEIGHT), GUILayout.Width(100)
            );
            
            EditorGUILayout.EndHorizontal();
            
            // Translate in editor
            EditorGUILayout.BeginHorizontal(HorizontalStyle);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Translate In Editor", LabelStylePrefix, GUILayout.Height(_LINE_HEIGHT));
            _localizationConfig.TranslateInEditor = EditorGUILayout.Toggle(
                _localizationConfig.TranslateInEditor, 
                ToggleStyle,
                GUILayout.Height(_LINE_HEIGHT), GUILayout.Width(100)
            );
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawKeys()
        {
            EditorGUILayout.Space(_LINE_HEIGHT);
            _keysScrollPosition = GUILayout.BeginScrollView(_keysScrollPosition, GUILayout.ExpandHeight(true));

            for (var i = 0; i < _localizationKeysData.Keys.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                var keyText = _localizationKeysData.Keys[i];
                
                var hasDoubles = HasDoubles(keyText);
                if (hasDoubles)
                {
                    GUI.color = Color.red;
                }
                
                _localizationKeysData.Keys[i] = EditorGUILayout.TextField(keyText, KeyStyle, GUILayout.Height(_LINE_HEIGHT));

                if (hasDoubles)
                {
                    GUI.color = Color.white;
                }
                
                _localizationKeysData.Keys = _localizationKeysData.Keys.Where(key => key != "").ToList();
                _localizationKeysData.Keys[i] = _localizationKeysData.Keys[i].Trim();

                if (GUILayout.Button("Remove", ButtonStyle, GUILayout.Width(70), GUILayout.Height(_LINE_HEIGHT)))
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

            if (LocalizeEditor.CanAddNewKey(_newKey) == false)
            {
                GUI.color = Color.red;    
            }
            
            _newKey = EditorGUILayout.TextField(_newKey, KeyStyle, GUILayout.Height(_LINE_HEIGHT));
            
            if (LocalizeEditor.CanAddNewKey(_newKey) == false)
            {
                EditorGUI.BeginDisabledGroup(true);
            }
            
            GUI.color = Color.white;
            
            if (GUILayout.Button("Add New Key", ButtonStyle,GUILayout.Height(_LINE_HEIGHT)))
            {
                _localizationKeysData.Keys.Add(_newKey.ToCorrectLocalizationKeyName());
                _keysScrollPosition = new Vector2(0, _MAX_FIELD_HEIGHT);
                
                _newKey = "";
            }
            
            if (LocalizeEditor.CanAddNewKey(_newKey) == false)
            {
                EditorGUI.EndDisabledGroup();
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private bool HasDoubles(string keyText)
        {
            return _localizationKeysData.Keys.Count(l => l == keyText) > 1;
        }

        private bool HasDoubles(Object objectKey, string language)
        {
             return objectKey == null || _localizationKeysData.ObjectsTranslations[language].ContainsKey(objectKey);
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
            
            GUILayout.Label("Add multiple keys (separated by commas or line breaks):", LabelStyle);
            EditorGUILayout.Space(10);
            
            var keysArray = _newKeys.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            var scrollHeight = (keysArray.Length) * _LINE_HEIGHT;
            
            if (scrollHeight > _MAX_FIELD_HEIGHT)
            {
                scrollHeight = _MAX_FIELD_HEIGHT;
            }
            
            _multipleKeysScrollPosition = GUILayout.BeginScrollView(_multipleKeysScrollPosition, GUILayout.Height(scrollHeight));
            
            _newKeys = EditorGUILayout.TextArea(_newKeys, TextAreaStyle);
            
            GUILayout.EndScrollView();

            if (keysArray.Length == 0)
            {
                EditorGUI.BeginDisabledGroup(true);
            }
                
            if (GUILayout.Button("Add Range", ButtonStyle,GUILayout.Height(_LINE_HEIGHT)))
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
                var keyName = key.ToCorrectLocalizationKeyName();
                if (keyName == "")
                {
                    continue;
                }
                
                _localizationKeysData.Keys.Add(keyName);
            }

            _newKeys = "";
            _newKey = "";

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

            if (GUILayout.Button("Generate Keys", ButtonStyle,GUILayout.Height(_LINE_HEIGHT)))
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
                EditorGUILayout.Space(_LINE_HEIGHT);
                _keysScrollPosition = GUILayout.BeginScrollView(_keysScrollPosition, GUILayout.ExpandHeight(true));
                
                for (var i = 0; i < translating.Count; i++)
                {
                    var key = translating.Keys.ElementAt(i);
                    var content = translating[key];
            
                    var textAreaHeight = KeyStyleMultiline.CalcHeight(new GUIContent(content), EditorGUIUtility.currentViewWidth * 0.7f);
                    textAreaHeight = Mathf.Max(textAreaHeight, _LINE_HEIGHT);

                    EditorGUILayout.BeginHorizontal();
            
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.TextField(key, KeyStyle, GUILayout.Height(textAreaHeight));
                    EditorGUI.EndDisabledGroup();

                    translating[key] = EditorGUILayout.TextArea(
                        content,
                        KeyStyleMultiline,
                        GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.7f),
                        GUILayout.Height(textAreaHeight));
            
                    EditorGUILayout.EndHorizontal();
            
                    EditorGUILayout.Space();
                }
                
                GUILayout.EndScrollView();
                
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                if (GUILayout.Button("Save to JSON", ButtonStyle,GUILayout.Height(_LINE_HEIGHT)))
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
            
            if (GUILayout.Button("Load from JSON", ButtonStyle,GUILayout.Height(_LINE_HEIGHT)))
            {
                _localizationKeysData.Translations = LocalizeEditor.GetAllLocalizationsDictionary();
                _localizationKeysData.Keys = _localizationKeysData.Translations.First().Value.Keys.ToList();
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
            
            var lastSelectedTab = _selectedLanguageTab;
            _selectedLanguageTab = GUILayout.Toolbar(_selectedLanguageTab, _languages.Select(l => l.i18nLang).ToArray(), ButtonStyle);

            if (_selectedLanguageTab != lastSelectedTab)
                GUI.FocusControl(null);
            
            EditorGUILayout.Space();
            
            return true;
        }

        private void DrawObject(Type type)
        {
            if (DrawTranslatingLanguageTabs() == false) return;

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();

            int height;
            int width;

            if (type == typeof(Sprite))
            {
                height = _LINE_HEIGHT * 4;
                width = _LINE_HEIGHT * 4;
            }
            else
            {
                height = _LINE_HEIGHT * 2;
                width = _LINE_HEIGHT * 8;
            }
            var language = _languages[_selectedLanguageTab].i18nLang;
                
            var hasDoubles = HasDoubles(_newObjectKey, language);
            if (hasDoubles)
            {
                GUI.color = Color.red;
            }
                
            _newObjectKey = EditorGUILayout.ObjectField(_newObjectKey, type, false, GUILayout.Height(height), GUILayout.Width(width));

            if (hasDoubles)
            {
                GUI.color = Color.white;
            }
            
            if (_newObjectKey != null && _newObjectKey.GetType() != type)
            {
                _newObjectKey = null;
            }
                
            EditorGUI.BeginDisabledGroup(_newObjectKey == null || hasDoubles);
            if (GUILayout.Button("Add", ButtonStyle,GUILayout.Height(_LINE_HEIGHT)))
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
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            
            if (_localizationKeysData.ObjectsTranslations.TryGetValue(language, out var translating))
            {
                var sortedTranslating = translating
                    .Where(t => t.Key.GetType() == type)
                    .ToDictionary(t => t.Key, t => t.Value);
                
                for (var i = 0; i < sortedTranslating.Count; i++)
                {
                    var key = sortedTranslating.ElementAt(i).Key;

                    EditorGUILayout.BeginHorizontal();
                    
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ObjectField(key, key.GetType(), false, GUILayout.Height(height), GUILayout.Width(width));
                    EditorGUI.EndDisabledGroup();

                    translating[key] = EditorGUILayout.ObjectField(sortedTranslating[key], key.GetType(), false, GUILayout.Height(height), GUILayout.Width(width));
                    
                    if (GUILayout.Button("Remove", ButtonStyle,GUILayout.Height(_LINE_HEIGHT)))
                    {
                        foreach (var lang in _languages)
                        {
                            _localizationKeysData.ObjectsTranslations[lang.i18nLang].Remove(key);
                        }

                        i--;
                    }
                    
                    EditorGUILayout.EndHorizontal();   
                }
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
