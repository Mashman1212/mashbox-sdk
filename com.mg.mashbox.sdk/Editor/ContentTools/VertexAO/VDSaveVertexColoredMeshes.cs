/*
VertexDirt plug-in for Unity
Copyright 2014-2020, Zoltan Farago, All rights reserved.
*/
using UnityEditor;
using UnityEngine;

class VDSaveVertexColoredMeshes : EditorWindow {

	private string path = "MashBox/VertexColors";
	static  VDSaveVertexColoredMeshes window;

	[MenuItem ("MashBox/Content Tools/Vertex AO Diagnostics/Export mesh assets", false, 20)]

	public static void Init() {
		if (!window){
			window = ScriptableObject.CreateInstance<VDSaveVertexColoredMeshes>();
			window.position = new Rect(200,200, 640,210);
			window.minSize = new Vector2 (640,210);
			window.maxSize = new Vector2 (10000,10000);
			#if UNITY_5_0 || UNITY_5_1 || UNITY_5_2 || UNITY_5_3 || UNITY_5_4 || UNITY_5_5_OR_NEWER
			window.titleContent = new GUIContent("MashBox Vertex Colors");
			#else
			window.title = "MashBox Vertex Colors";
			#endif
			window.ShowUtility();
		}
	}

	void OnGUI() {
		if (GUILayout.Button("Open precision baker / Overwrite original FBX", GUILayout.Height(28))) VertexDirtWindow.ShowWindow();
		EditorGUILayout.Space();
		GUILayout.Label("This tool collects the sharedMeshes of the Selection's Mesh Filter components, and saves them to .asset files.");
		GUILayout.Label("Then you can use the saved meshes with the generated colors without the need of the scene color component.");
		GUILayout.Label("");
		GUILayout.Label("Select single GameObject.");
		path = EditorGUILayout.TextField("Asset path for saving: ", path);
		using (new EditorGUI.DisabledScope(!Selection.activeTransform))
		if (GUILayout.Button("Save meshes of children.", GUILayout.Height(40))) {
			//Debug.Log (GetPathName(Selection.activeTransform, ""));
			Transform[]  gos = Selection.activeTransform.GetComponentsInChildren<Transform>();

			foreach (Transform t in gos) {

				if (t.gameObject.GetComponent<VDColorHandlerBase>() && t.gameObject.GetComponent<MeshFilter>()) {

					try {
						AssetDatabase.CreateAsset (t.gameObject.GetComponent<MeshFilter>().sharedMesh, "Assets/"+path+"/"+GetPathName(t, "") +".asset");
					}
					catch(UnityException e) {
						Debug.Log (e+"\nThis asset already saved. If you have multiple gameobjects at the same hierarchy and with the same name, please give them uniqe names.");
					}
					AssetDatabase.SaveAssets();
					t.gameObject.GetComponent<VDColorHandlerBase>().coloredMesh =
					t.gameObject.GetComponent<MeshFilter>().sharedMesh;

				}

			}
			AssetDatabase.Refresh();
		}
		Repaint();
	}

	public string GetPathName(Transform t, string s){
		s = t.name + s;
		if (t.parent != null) {
			s = "--" + s;
			s = GetPathName(t.parent, s);
		}
		return s;
	}
}
