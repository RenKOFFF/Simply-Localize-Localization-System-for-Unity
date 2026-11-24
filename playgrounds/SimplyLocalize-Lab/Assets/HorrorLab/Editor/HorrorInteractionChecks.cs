using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SimplyLocalize.HorrorLab.Editor
{
    public static class HorrorInteractionChecks
    {
        public static string Run()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            var fixture = new GameObject("Temporary interaction checks");
            int passed = 0;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); passed++; }
            try
            {
                fixture.transform.position = new Vector3(1000, 10, 1000);
                var hero = new GameObject("Hero").transform; hero.SetParent(fixture.transform, false);
                HorrorInteractable Prop(string name, Vector3 position, HorrorInteraction kind = HorrorInteraction.Key)
                {
                    var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name;
                    obj.transform.SetParent(fixture.transform, false); obj.transform.localPosition = position; obj.transform.localScale = Vector3.one * .15f;
                    var target = obj.AddComponent<HorrorInteractable>(); target.kind = kind; return target;
                }
                var front = Prop("Front", new Vector3(.4f, 1.4f, 1.2f));
                var back = Prop("Behind", new Vector3(0, 1.4f, -.5f));
                var side = Prop("Side", new Vector3(.8f, 1.4f, .1f));
                var far = Prop("Too far", new Vector3(0, 1.4f, 4));
                var sign = Prop("Sign", new Vector3(0, 1.4f, .7f), HorrorInteraction.Sign);
                sign.GetComponent<Collider>().enabled = false;
                var all = new[] { front, back, side, far, sign };
                Physics.SyncTransforms();
                Check(HorrorInteractionQuery.Find(hero, all) == front, "Off-axis object in front of hero must be selected");
                Check(HorrorInteractionQuery.Find(hero, new[] { back, side, far }) == null, "Behind, side and distant props must be excluded");
                sign.GetComponent<Collider>().enabled = true;
                Check(HorrorInteractionQuery.Find(hero, new[] { sign }) == null, "Sign must not take the interact button");
                sign.gameObject.SetActive(false);
                var second = Prop("Second", new Vector3(.4f, 1.4f, 1.32f));
                Physics.SyncTransforms();
                Check(HorrorInteractionQuery.Find(hero, new[] { second, front }) == front, "Nearest eligible prop wins");
                // Put it alongside, so the front prop does not physically occlude the second.
                second.transform.localPosition = new Vector3(-.4f, 1.4f, 1.25f); Physics.SyncTransforms();
                Check(HorrorInteractionQuery.Find(hero, new[] { front, second }, second) == second, "Small distance changes retain the current target");
                second.transform.localPosition = new Vector3(-.4f, 1.4f, 1.8f); Physics.SyncTransforms();
                Check(HorrorInteractionQuery.Find(hero, new[] { front, second }, second) == front, "Hysteresis must release a clearly farther target");
                second.gameObject.SetActive(false);
                front.enabled = false;
                Check(HorrorInteractionQuery.Find(hero, all) == null, "Disabled interaction component excluded");
                front.enabled = true; front.GetComponent<Collider>().enabled = false;
                Check(HorrorInteractionQuery.Find(hero, all) == null, "Disabled collider excluded");
                front.GetComponent<Collider>().enabled = true;
                front.gameObject.SetActive(false);
                Check(HorrorInteractionQuery.Find(hero, all) == null, "Inactive pickup excluded");
                front.gameObject.SetActive(true);
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(fixture.transform, false);
                wall.transform.localPosition = new Vector3(.2f, 1.4f, .6f); wall.transform.localScale = new Vector3(1.5f, 2, .15f);
                Physics.SyncTransforms();
                Check(HorrorInteractionQuery.Find(hero, all) == null, "Cannot interact through a wall");
                wall.SetActive(false); Physics.SyncTransforms();
                front.Consume();
                Check(HorrorInteractionQuery.Find(hero, all) == null, "Consumed pickup excluded immediately");
                front.gameObject.SetActive(false);
                var hinge = new GameObject("Door hinge"); hinge.transform.SetParent(fixture.transform, false); hinge.transform.localPosition = new Vector3(-1, 0, 1.5f);
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube); leaf.transform.SetParent(hinge.transform, false); leaf.transform.localPosition = new Vector3(1, 1.4f, 0); leaf.transform.localScale = new Vector3(2, 2.8f, .15f);
                var door = hinge.AddComponent<HorrorInteractable>(); door.kind = HorrorInteraction.Door;
                Physics.SyncTransforms();
                Check(Mathf.Abs(door.InteractionPoint.x - hero.position.x) < .01f, "Door anchor must be leaf centre, not hinge");
                Check(HorrorInteractionQuery.Find(hero, new[] { door }) == door, "Door reachable from the front");
                hinge.SetActive(false);
                var table = GameObject.CreatePrimitive(PrimitiveType.Cube); table.transform.SetParent(fixture.transform, false); table.transform.localPosition = new Vector3(0, .4f, 1.3f); table.transform.localScale = new Vector3(1, .8f, 1);
                var key = Prop("Table key", new Vector3(0, .9f, 1.25f)); Physics.SyncTransforms();
                Check(HorrorInteractionQuery.Find(hero, new[] { key }) == key, "Key on table is reachable from standing height");
                table.SetActive(false); key.gameObject.SetActive(false);
                sign.gameObject.SetActive(true); sign.transform.localPosition = new Vector3(.6f, 2.3f, 3);
                var cameraObject = new GameObject("Inspection camera", typeof(Camera)); cameraObject.transform.SetParent(fixture.transform, false); cameraObject.transform.localPosition = new Vector3(0, 1.6f, -.5f);
                Physics.SyncTransforms();
                Check(HorrorInteractionQuery.FindSign(hero, cameraObject.GetComponent<Camera>(), all) == sign, "Read a sign without exact centre alignment");
                wall.SetActive(true); wall.transform.localPosition = new Vector3(.3f, 1.8f, 1.5f); wall.transform.localScale = new Vector3(3, 3, .2f); Physics.SyncTransforms();
                Check(HorrorInteractionQuery.FindSign(hero, cameraObject.GetComponent<Camera>(), all) == null, "Wall blocks sign inspection too");
                return "PASS: " + passed + " interaction cases (front cone, distance, nearest, hysteresis, disabled/consumed props, walls, door hinge, tabletop key, independent sign inspection).";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture); Physics.SyncTransforms();
            }
        }

        public static string SceneTargets()
        {
            var game = UnityEngine.Object.FindFirstObjectByType<HorrorPlayground>();
            if (game == null) throw new InvalidOperationException("Open NightShift scene.");
            var scene = game.gameObject.scene; bool wasDirty = scene.isDirty;
            var position = game.player.transform.position; var rotation = game.player.transform.rotation;
            var cameraRotation = game.view.transform.rotation;
            var cameraPosition = game.view.transform.position;
            bool controllerEnabled = game.player.enabled;
            int count = 0;
            try
            {
                game.player.enabled = false;
                foreach (var target in game.interactables.Where(t => t.kind != HorrorInteraction.Sign && t.IsAvailable))
                {
                    var point = target.InteractionPoint;
                    game.player.transform.position = new Vector3(point.x, 0, point.z - 1.1f);
                    game.player.transform.rotation = Quaternion.identity;
                    // Deliberately aim the camera away: world interactions depend on the hero.
                    game.view.transform.rotation = Quaternion.Euler(0, 180, 0);
                    Physics.SyncTransforms();
                    var selected = HorrorInteractionQuery.Find(game.player.transform, game.interactables);
                    if (selected != target) throw new InvalidOperationException("Actual scene target not reachable: " + target.name);
                    count++;
                }
                return "PASS: " + count + " real scene targets reachable in front of hero with camera facing away.";
            }
            finally
            {
                game.player.transform.SetPositionAndRotation(position, rotation); game.player.enabled = controllerEnabled;
                game.view.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                Physics.SyncTransforms();
            }
        }
    }
}

