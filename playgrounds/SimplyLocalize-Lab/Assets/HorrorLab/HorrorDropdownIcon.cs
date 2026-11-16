using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.HorrorLab
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HorrorDropdownIcon : MaskableGraphic
    {
        public bool checkmark;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = GetPixelAdjustedRect();
            Vector2 Point(float x, float y) => new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
            if (!checkmark)
            {
                mesh.AddVert(Point(.15f, .7f), color, Vector2.zero);
                mesh.AddVert(Point(.85f, .7f), color, Vector2.zero);
                mesh.AddVert(Point(.5f, .3f), color, Vector2.zero);
                mesh.AddTriangle(0, 1, 2); return;
            }
            void Stroke(Vector2 a, Vector2 b)
            {
                var normal = new Vector2(-(b - a).y, (b - a).x).normalized * Mathf.Min(rect.width, rect.height) * .065f;
                int index = mesh.currentVertCount;
                mesh.AddVert(a - normal, color, Vector2.zero); mesh.AddVert(a + normal, color, Vector2.zero);
                mesh.AddVert(b + normal, color, Vector2.zero); mesh.AddVert(b - normal, color, Vector2.zero);
                mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
            }
            Stroke(Point(.12f, .52f), Point(.4f, .25f)); Stroke(Point(.4f, .25f), Point(.9f, .8f));
        }
    }
}
