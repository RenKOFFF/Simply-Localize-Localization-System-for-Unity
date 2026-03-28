using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Utilities
{
    public static class TextFieldExtensions
    {
        public static void SetPlaceholderText(this TextField textField, string placeholder)
        {
            var textInput = textField.Q<TextElement>();
            if (textInput == null) return;

            void OnFocusIn(FocusInEvent evt)
            {
                if (textField.value == placeholder)
                {
                    textField.value = "";
                    textInput.style.color = StyleKeyword.Null;
                }
            }

            void OnFocusOut(FocusOutEvent evt)
            {
                if (string.IsNullOrEmpty(textField.value))
                {
                    textField.value = placeholder;
                    textInput.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
                }
            }

            textField.RegisterCallback<FocusInEvent>(OnFocusIn);
            textField.RegisterCallback<FocusOutEvent>(OnFocusOut);

            if (string.IsNullOrEmpty(textField.value))
            {
                textField.value = placeholder;
                textInput.style.color = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
            }
        }
    }
}
