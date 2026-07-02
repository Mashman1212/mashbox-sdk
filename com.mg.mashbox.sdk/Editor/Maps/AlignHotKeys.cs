#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace MashBoxBridge.Environment.Tools.Editor
{
    public class AlignHotKeys : UnityEditor.Editor
    {
        [MenuItem("Tools/MashBox/Alignment/Align to surface %h")]
        private static void Align()
        {
            if (Selection.activeTransform != null && Selection.gameObjects.Length > 0 && Selection.gameObjects[0] != null)
            {
                RaycastHit raycastHit;
                Ray ray;
                ray = new Ray();
                ray.origin = Selection.activeTransform.position;
                ray.direction = Vector3.down;

                bool rayHit = Physics.Raycast(ray, out raycastHit, 10.0f);

                if (!rayHit)
                {
                    ray.direction *= -1.0f;
                    rayHit = Physics.Raycast(ray, out raycastHit, 10.0f);
                }
                
                
                if (rayHit)
                {

                    if (Selection.gameObjects[0].GetComponent<UnityEngine.Rendering.HighDefinition.DecalProjector>())
                    {
                        Undo.RecordObject(Selection.gameObjects[0].transform, "Align");
                        Selection.gameObjects[0].transform.forward = -raycastHit.normal;
                    }
                    else
                    {
                        Undo.RecordObject(Selection.gameObjects[0].transform, "Align");
                        Selection.gameObjects[0].transform.up = raycastHit.normal;
                    }
                }
            }
        }
    
        [MenuItem("Tools/MashBox/Alignment/Align to surface local foward %e")]
        private static void AlignToForward()
        {
            if (Selection.activeTransform != null && Selection.gameObjects.Length > 0 && Selection.gameObjects[0] != null)
            {
                foreach (Transform trans in Selection.transforms)
                {
                    AlignTransformToForward(trans);
                }
            }
        }

        static void AlignTransformToForward(Transform trans)
        {
            if (trans != null)
            {
                RaycastHit raycastHit;
                Ray ray;
                ray = new Ray();
                ray.origin = trans.position - (Selection.activeTransform.forward * .1f);
                ray.direction = trans.forward;

                bool rayHit = Physics.Raycast(ray, out raycastHit, 10.0f);

                if (rayHit)
                {
                    Undo.RecordObject (Selection.gameObjects[0].transform, "AlignToNormal");

                    Undo.RecordObject(trans, "AlignTransformToForward");
                
                    if (trans.GetComponent<UnityEngine.Rendering.HighDefinition.DecalProjector>())
                    {
                        Vector3 forward = -raycastHit.normal;
                        Vector3 up = Vector3.Cross(Vector3.Cross( Vector3.up,raycastHit.normal),raycastHit.normal);

                        trans.rotation = Quaternion.LookRotation(forward, up);
                    }
                    else
                    {
                        trans.rotation =
                            Quaternion.LookRotation(-raycastHit.normal,Vector3.up);
                    }
                
                    if(trans.up.y < 0.0f)
                    {
                        trans.rotation *= Quaternion.AngleAxis(180.0f,Vector3.forward);
                    }
                }
            }
        }

        [MenuItem("Tools/MashBox/Alignment/Center Decal Projector Pivot #y")]
        static void CenterDecalProjectorPivot()
        {
            if (Selection.activeObject != null)
            {
                DecalProjector decalProjector = Selection.gameObjects[0].GetComponent<UnityEngine.Rendering.HighDefinition.DecalProjector>();
            
                if (decalProjector)
                {
                    Undo.RecordObject(decalProjector.transform, "TranslateDecalProject");
                    decalProjector.transform.Translate(decalProjector.pivot.x,decalProjector.pivot.y,0.0f,Space.Self);
                    Undo.RecordObject(decalProjector, "CenterDecalProjectorPivot");
                    decalProjector.pivot = Vector3.zero + Vector3.forward * .5f * decalProjector.size.z;
                }
            }
        }
    }
}

#endif