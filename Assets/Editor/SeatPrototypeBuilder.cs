using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.InputSystem.UI;
using TMPro;

namespace Justitia.Editor
{
    public static class SeatPrototypeBuilder
    {
        private static Material guest, host, stone, gold, dark, white;
        private static TMP_FontAsset font;

        [MenuItem("Justitia/Create Seat Character Prototype")]
        public static void Build()
        {
            if (GameObject.Find("SeatCharacterPrototype"))
                throw new System.InvalidOperationException("SeatCharacterPrototype already exists; edit it in place.");
            System.IO.Directory.CreateDirectory("Assets/Players/Materials");
            System.IO.Directory.CreateDirectory("Assets/Players/Prefabs");
            AssetDatabase.Refresh();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (!font) throw new System.InvalidOperationException("Import TMP essentials first.");
            guest = Mat("Guest_Teal", new Color(0.08f, 0.7f, 0.76f));
            host = Mat("Host_Amber", new Color(1f, 0.52f, 0.12f));
            stone = Mat("Court_Stone", new Color(0.27f, 0.32f, 0.4f));
            gold = Mat("Scale_Brass", new Color(0.66f, 0.48f, 0.22f));
            dark = Mat("Court_Dark", new Color(0.07f, 0.1f, 0.16f));
            white = Mat("Eye_White", new Color(0.9f, 0.95f, 1));

            var root = new GameObject("SeatCharacterPrototype");
            Undo.RegisterCreatedObjectUndo(root, "Create seat character prototype");
            Cube("CourtFloor", root.transform, new Vector3(0,-0.4f,3), new Vector3(24,0.5f,22), dark);
            Cube("ScaleBase", root.transform, new Vector3(0,0,2), new Vector3(3,0.5f,3), stone);
            Cube("ScalePillar", root.transform, new Vector3(0,2.6f,2), new Vector3(0.45f,5,0.45f), gold);
            Cube("ScaleBeam", root.transform, new Vector3(0,5.1f,2), new Vector3(10.8f,0.3f,0.4f), gold);
            // A simple cube marker establishes the shared forward direction, toward the goddess.
            Cube("GoddessPlaceholder", root.transform, new Vector3(0,4,7), new Vector3(2.6f,7,1.5f), stone);
            Cube("GoddessHead", root.transform, new Vector3(0,8.4f,7), new Vector3(2,2,1.7f), stone);
            Cube("Blindfold", root.transform, new Vector3(0,8.55f,6.12f), new Vector3(2.1f,0.38f,0.12f), gold);

            var g = MakeSeat(root.transform, PlayerRole.Guest, -4.5f, guest);
            var h = MakeSeat(root.transform, PlayerRole.Host, 4.5f, host);
            var camera = Camera.main;
            if (!camera) camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            Undo.RecordObject(camera.transform, "Frame seat prototype");
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0,7,-13);
            camera.transform.LookAt(new Vector3(0,3.3f,2.5f));
            camera.fieldOfView = 57;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 150;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f,0.035f,0.065f);
            var controller = root.AddComponent<MouseSeatView>();
            controller.Guest = g; controller.Host = h; controller.ViewCamera = camera;
            MakeUI(root.transform, controller);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.LookAt(new Vector3(0,3,2), camera.transform.rotation, 18);
        }

        private static Material Mat(string name, Color color)
        {
            var path = "Assets/Players/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            mat.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static SeatPlayer MakeSeat(Transform root, PlayerRole role, float x, Material material)
        {
            var plate = new GameObject(role + "Plate").transform;
            plate.SetParent(root, false); plate.localPosition = new Vector3(x,1.2f,2);
            Cube("Plate", plate, Vector3.zero, new Vector3(3.3f,0.25f,3), gold);
            Cube("RoleStripe", plate, new Vector3(0,0,-1.52f), new Vector3(3.3f,0.2f,0.08f), material);
            foreach (float side in new[] {-1.4f,1.4f})
                Cube("Suspension", root, new Vector3(x+side,3.2f,2), new Vector3(0.07f,3.8f,0.07f), gold);
            var anchor = new GameObject("SeatAnchor").transform;
            anchor.SetParent(plate, false); anchor.localPosition = new Vector3(0,0.13f,0);
            anchor.localRotation = Quaternion.LookRotation(new Vector3(-x,0,5));
            var player = new GameObject(role + "Character");
            var model = new GameObject("CubeModel").transform;
            model.SetParent(player.transform, false);
            Cube("Body", model, new Vector3(0,0.65f,0), new Vector3(0.9f,1.3f,0.7f), material);
            Cube("Head", model, new Vector3(0,1.65f,0), new Vector3(0.85f,0.7f,0.8f), material);
            foreach (float side in new[] {-0.22f,0.22f})
            {
                Cube("Eye", model, new Vector3(side,1.7f,0.405f), new Vector3(0.18f,0.18f,0.06f), white);
                Cube("Pupil", model, new Vector3(side,1.7f,0.442f), new Vector3(0.075f,0.09f,0.025f), dark);
            }
            var eye = new GameObject("EyeAnchor").transform;
            eye.SetParent(player.transform, false); eye.localPosition = new Vector3(0,1.72f,0.48f);
            var component = player.AddComponent<SeatPlayer>();
            component.Role = role; component.Seat = role == PlayerRole.Guest ? SeatId.Left : SeatId.Right;
            component.Eye = eye; component.Visuals = player.GetComponentsInChildren<Renderer>();
            var prefabPath = "Assets/Players/Prefabs/" + role + "Character.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(player, prefabPath, InteractionMode.AutomatedAction);
            player.transform.SetParent(anchor, false);
            component.SeatAnchor = anchor;
            component.FollowSeat();
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            var label = new GameObject(role + "Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(plate, false); label.transform.localPosition = new Vector3(0,0.45f,-1.65f);
            label.font = font; label.text = role.ToString().ToUpperInvariant(); label.fontSize = 4;
            label.alignment = TextAlignmentOptions.Center; label.color = material.color;
            label.rectTransform.sizeDelta = new Vector2(3,0.6f);
            return component;
        }

        private static void MakeUI(Transform root, MouseSeatView controller)
        {
            var go = new GameObject("CharacterPreviewUI", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            go.transform.SetParent(root, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280,720); scaler.matchWidthOrHeight = 0.5f;
            var panel = new GameObject("Controls", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.VerticalLayoutGroup));
            panel.transform.SetParent(go.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f,0);
            rect.anchoredPosition = new Vector2(0,18); rect.sizeDelta = new Vector2(720,160);
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0.025f,0.04f,0.07f,0.96f);
            var layout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(20,20,12,12); layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            controller.StatusLabel = Label(panel.transform,"CHARACTER PREVIEW  |  Choose Guest or Host",20,28);
            var row = new GameObject("ViewButtons", typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup), typeof(UnityEngine.UI.LayoutElement));
            row.transform.SetParent(panel.transform,false);
            row.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 42;
            var horizontal = row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            horizontal.spacing=8; horizontal.childControlWidth=true; horizontal.childControlHeight=true; horizontal.childForceExpandWidth=true;
            Button(row.transform,"Guest", guest.color, controller.ShowGuest);
            Button(row.transform,"Host", host.color, controller.ShowHost);
            Button(row.transform,"Overview",new Color(0.45f,0.53f,0.65f),controller.ShowOverview);
            Button(row.transform,"Reset view",new Color(0.45f,0.53f,0.65f),controller.ResetView);
            Label(panel.transform,"MOVE MOUSE: LOOK  /  NO MOVEMENT OR JUMP",15,24);
            if (!Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>())
            {
                var events = new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(InputSystemUIInputModule));
                events.transform.SetParent(root,false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
        }

        private static TMP_Text Label(Transform parent,string text,int size,float height)
        {
            var go = new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI),typeof(UnityEngine.UI.LayoutElement));
            go.transform.SetParent(parent,false);
            var label=go.GetComponent<TextMeshProUGUI>(); label.font=font; label.text=text; label.fontSize=size;
            label.alignment=TextAlignmentOptions.Center; label.color=Color.white; label.raycastTarget=false;
            go.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight=height;
            return label;
        }

        private static void Button(Transform parent,string text,Color color,UnityEngine.Events.UnityAction action)
        {
            var go=new GameObject(text+"Button",typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));
            go.transform.SetParent(parent,false); go.GetComponent<UnityEngine.UI.Image>().color=color;
            var button=go.GetComponent<UnityEngine.UI.Button>();
            button.navigation=new UnityEngine.UI.Navigation { mode=UnityEngine.UI.Navigation.Mode.None };
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick,action);
            var label=Label(go.transform,text,18,40); label.color=new Color(0.02f,0.03f,0.06f);
            label.rectTransform.anchorMin=Vector2.zero; label.rectTransform.anchorMax=Vector2.one;
            label.rectTransform.offsetMin=label.rectTransform.offsetMax=Vector2.zero;
        }
    }
}
