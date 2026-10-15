
//	VertexDirt plug-in for Unity
//	Copyright 2014-2020, Zoltan Farago, All rights reserved.

using UnityEngine;
using System.Collections;

public class VDColorHandlerBase : MonoBehaviour
{
	[HideInInspector]
	public Color32[] colors = new Color32[0]; 
	[HideInInspector]
	public Color32[] tempColors = new Color32[0]; 
	[HideInInspector]
	public Mesh coloredMesh;
	public Mesh originalMesh;
}
